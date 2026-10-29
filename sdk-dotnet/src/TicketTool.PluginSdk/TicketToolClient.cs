using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TicketTool.PluginSdk
{
    /// <summary>Answers one lookup. Called on a pool thread; hop to the game thread to read state.</summary>
    public delegate Task<LookupResult> LookupHandler(LookupRequest request, CancellationToken cancellation);

    /// <summary>Carries out one player action. Called on a pool thread; hop to the game thread to act.</summary>
    public delegate Task<ActionResult> ActionHandler(ActionRequest request, CancellationToken cancellation);

    public sealed class TicketToolClientOptions
    {
        public TicketToolClientOptions()
        {
            ApiUrl = TicketToolClient.DefaultApiUrl;
            BanProviders = () => new string[0];
            Actions = () => new string[0];
            ReportsEnabled = () => false;
            Logger = NullSdkLogger.Instance;
            Clock = () => DateTimeOffset.UtcNow;
            Delay = (wait, token) => Task.Delay(wait, token);
            Random = new Random();
            AnswerBudget = TimeSpan.FromSeconds(3);
        }

        /// <summary>The full integration key; only the derived signing secret is kept.</summary>
        public string Key { get; set; }

        /// <summary>Host root such as https://dash.ticket-tool.app, or a base ending in /api/plugin/v1.</summary>
        public string ApiUrl { get; set; }

        public PluginInfo Plugin { get; set; }
        public LookupHandler Handler { get; set; }

        /// <summary>Read at each /hello, e.g. ["simpleadmin", "server"].</summary>
        public Func<IList<string>> BanProviders { get; set; }

        /// <summary>Carries out player.action requests. Without it every action is answered not_allowed.</summary>
        public ActionHandler ActionHandler { get; set; }

        /// <summary>
        /// The actions the server owner turned on, read at each /hello and checked for
        /// every request; anything else is answered not_allowed.
        /// </summary>
        public Func<IList<string>> Actions { get; set; }

        /// <summary>Whether in-game reports are on, read at each /hello and before each report.</summary>
        public Func<bool> ReportsEnabled { get; set; }

        public ISdkLogger Logger { get; set; }

        /// <summary>Time allowed per answer before a timeout error is sent instead; capped by the request's expiry.</summary>
        public TimeSpan AnswerBudget { get; set; }

        /// <summary>Defaults to HttpClientSender; the Rust build passes a WebRequestSender.</summary>
        public IHttpSender Sender { get; set; }

        public Func<DateTimeOffset> Clock { get; set; }
        public Func<TimeSpan, CancellationToken, Task> Delay { get; set; }
        public Random Random { get; set; }
    }

    /// <summary>
    /// A Ticket Tool game plugin client: /hello, one long-poll loop on the thread
    /// pool, answers on pool threads, and /goodbye on Stop. Every await uses
    /// ConfigureAwait(false) so nothing is posted back to a game's main thread,
    /// which also keeps the blocking Stop safe to call from it.
    /// </summary>
    public sealed class TicketToolClient : IDisposable
    {
        public const int ProtocolVersion = 1;
        public const string DefaultApiUrl = "https://dash.ticket-tool.app";
        internal const string ApiPath = "/api/plugin/v1";

        internal static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);
        internal static readonly TimeSpan ActionBudget = TimeSpan.FromSeconds(8);
        internal static readonly TimeSpan GoodbyeTimeout = TimeSpan.FromSeconds(2);
        internal static readonly TimeSpan PlanRetry = TimeSpan.FromHours(1);
        internal static readonly TimeSpan RateLimitWait = TimeSpan.FromMinutes(1);
        // A crashed process's poll lease lapses on the server within 40 s.
        internal static readonly TimeSpan TooManyPollsMinWait = TimeSpan.FromSeconds(15);

        private readonly PluginInfo _plugin;
        private readonly LookupHandler _handler;
        private readonly Func<IList<string>> _banProviders;
        private readonly ActionHandler _actionHandler;
        private readonly Func<IList<string>> _actions;
        private readonly Func<bool> _reportsEnabled;
        private readonly ISdkLogger _log;
        private readonly Func<DateTimeOffset> _clock;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly Random _random;
        private readonly object _randomLock = new object();
        private readonly TimeSpan _answerBudget;
        private readonly IHttpSender _http;
        private readonly Transport _transport;
        private readonly Backoff _backoff;
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();

        private readonly object _lock = new object();
        private Task _loop;
        private volatile bool _running;
        private volatile bool _closed;
        private volatile bool _everConnected;
        private volatile bool _needHello = true;

        private volatile ClientState _state = ClientState.Stopped;
        private volatile string _lastError;
        private DateTimeOffset? _lastErrorAt;
        private volatile HelloReply _hello;
        private DateTimeOffset? _lastPollAt;
        private long _answered;
        private int _failureStreak;

        public TicketToolClient(TicketToolClientOptions options)
        {
            if (options == null) throw new ArgumentNullException("options");
            if (options.Plugin == null) throw new ArgumentException("Plugin is required");
            if (options.Handler == null) throw new ArgumentException("Handler is required");
            Key = PluginKey.Parse(options.Key);
            Uri apiBase = ApiBase(options.ApiUrl);
            _plugin = options.Plugin;
            _handler = options.Handler;
            _banProviders = options.BanProviders ?? (() => new string[0]);
            _actionHandler = options.ActionHandler;
            _actions = options.Actions ?? (() => new string[0]);
            _reportsEnabled = options.ReportsEnabled ?? (() => false);
            _log = options.Logger ?? NullSdkLogger.Instance;
            _clock = options.Clock ?? (() => DateTimeOffset.UtcNow);
            _delay = options.Delay ?? ((wait, token) => Task.Delay(wait, token));
            _random = options.Random ?? new Random();
            _answerBudget = options.AnswerBudget;
            _http = options.Sender ?? DefaultSender();
            _transport = new Transport(_http, apiBase, Key, _plugin.UserAgent, _clock);
            _backoff = Backoff.Standard(_random);
        }

        public PluginKey Key { get; private set; }

        public ClientStatus Status
        {
            get
            {
                return new ClientStatus(_state, _lastError, _lastErrorAt, _hello, _lastPollAt,
                    Interlocked.Read(ref _answered), _transport.OffsetSeconds);
            }
        }

        // Kept in its own method so a build that leaves HttpClientSender out (the
        // Rust single file) only has to replace this one line.
        private static IHttpSender DefaultSender()
        {
            return new HttpClientSender(); // @default-sender
        }

        /// <summary>The URL every endpoint hangs off, from a host root or an already complete base.</summary>
        public static Uri ApiBase(string apiUrl)
        {
            string url = string.IsNullOrWhiteSpace(apiUrl) ? DefaultApiUrl : apiUrl.Trim();
            url = url.TrimEnd('/');
            if (!url.EndsWith(ApiPath, StringComparison.Ordinal)) url += ApiPath;
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
                throw new ArgumentException("api-url isn't a valid URL: " + apiUrl);
            string host = uri.Host;
            bool loopback = host == "localhost" || host.StartsWith("127.", StringComparison.Ordinal)
                            || host == "[::1]" || host == "::1";
            if (uri.Scheme != "https" && !(uri.Scheme == "http" && loopback))
                throw new ArgumentException("api-url must use https:// (plain http is only allowed for localhost)");
            return uri;
        }

        /// <summary>Starts the poll loop. Returns at once; /hello is sent from the loop.</summary>
        public void Start()
        {
            lock (_lock)
            {
                if (_closed) throw new InvalidOperationException("A stopped client can't be restarted; make a new one");
                if (_running) return;
                _running = true;
                _state = ClientState.Starting;
                _loop = Task.Run(() => RunLoopAsync());
            }
        }

        /// <summary>
        /// Stops polling and sends /goodbye so lookups show the plugin as offline
        /// at once. Blocks for at most about three seconds.
        /// </summary>
        public void Stop()
        {
            Task loop;
            lock (_lock)
            {
                if (_closed) return;
                _closed = true;
                _running = false;
                loop = _loop;
            }
            _stopping.Cancel();
            if (loop != null)
            {
                try { loop.Wait(TimeSpan.FromSeconds(1)); }
                catch (AggregateException) { }
            }
            if (_everConnected && _state != ClientState.StoppedInvalidKey)
            {
                try
                {
                    Task goodbye = Task.Run(() => _transport.CallAsync("/goodbye", new Dictionary<string, object>(),
                        GoodbyeTimeout, CancellationToken.None));
                    if (!goodbye.Wait(GoodbyeTimeout + TimeSpan.FromMilliseconds(500)))
                        _log.Debug("Goodbye not delivered: timed out");
                }
                catch (AggregateException e)
                {
                    _log.Debug("Goodbye not delivered: " + RootMessage(e));
                }
            }
            if (_state == ClientState.Starting || _state == ClientState.Connected
                || _state == ClientState.Reconnecting || _state == ClientState.PausedPlan)
                _state = ClientState.Stopped;
            _http.Dispose();
        }

        public void Dispose()
        {
            Stop();
        }

        /// <summary>
        /// Sends an in-game report. Pass the identity and name of the player who ran
        /// the command, never values from its arguments. Never throws.
        /// </summary>
        /// <param name="target">The player the report is about, when the command names one; may be null.</param>
        /// <param name="server">Server or map name; may be null.</param>
        public async Task<ReportResult> ReportAsync(RequestPlayer from, string message, RequestPlayer target = null,
            string server = null)
        {
            if (from == null) throw new ArgumentNullException("from");
            if (message == null) throw new ArgumentNullException("message");
            if (_closed) return new ReportResult(ReportOutcome.Failed, "not_running");
            if (!_reportsEnabled()) return new ReportResult(ReportOutcome.NotEnabled, "not_enabled");
            if (Text.EmptyToNull(message) == null) return new ReportResult(ReportOutcome.Failed, "empty_message");
            try
            {
                return Messages.ReportReply(await _transport.CallAsync("/event",
                    Messages.Report(from, message, target, server), CallTimeout, _stopping.Token).ConfigureAwait(false));
            }
            catch (ApiException e)
            {
                return Messages.ReportError(e);
            }
            catch (OperationCanceledException)
            {
                return new ReportResult(ReportOutcome.Failed, "not_running");
            }
            catch (Exception e)
            {
                _log.Warn("Couldn't reach Ticket Tool to send " + from.Name + "'s report: " + RootMessage(e), null);
                return new ReportResult(ReportOutcome.Failed, "network");
            }
        }

        /// <summary>Sends /hello again so the dashboard sees changed actions or reports, e.g. after a reload.</summary>
        public void RefreshHello()
        {
            _needHello = true;
        }

        private async Task RunLoopAsync()
        {
            CancellationToken stop = _stopping.Token;
            _needHello = true;
            while (_running)
            {
                try
                {
                    if (_needHello)
                    {
                        _needHello = false;
                        HelloReply reply;
                        try
                        {
                            reply = Messages.HelloReply(await _transport.CallAsync("/hello",
                                Messages.Hello(_plugin, _banProviders(),
                                    _actionHandler == null ? null : _actions(), _reportsEnabled()),
                                CallTimeout, stop).ConfigureAwait(false));
                        }
                        catch (Exception)
                        {
                            _needHello = true;
                            throw;
                        }
                        _hello = reply;
                        _everConnected = true;
                        _log.Info("Connected to Ticket Tool as \"" + reply.KeyName + "\" for Discord server "
                                  + (reply.ServerName ?? reply.ServerId) + " (" + Key + ")");
                    }
                    var pollTimeout = TimeSpan.FromSeconds(Math.Max(35, _hello.PollWaitSeconds + 10));
                    _lastPollAt = _clock();
                    _state = ClientState.Connected;
                    var body = await _transport.CallAsync("/poll", new Dictionary<string, object>(), pollTimeout, stop)
                        .ConfigureAwait(false);
                    Recovered();
                    foreach (LookupRequest request in Messages.PollRequests(body)) Dispatch(request);
                    foreach (ActionRequest action in Messages.PollActions(body)) DispatchAction(action);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    break;
                }
                catch (ApiException e)
                {
                    if (!_running) break;
                    if (await HandleApiErrorAsync(e).ConfigureAwait(false)) _needHello = true;
                }
                catch (TimeoutException e)
                {
                    if (!_running) break;
                    await FailedAsync("Ticket Tool didn't answer in time", e, _backoff.Next()).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    if (!_running) break;
                    await FailedAsync("Can't reach Ticket Tool: " + RootMessage(e), e, _backoff.Next())
                        .ConfigureAwait(false);
                }
            }
        }

        /// <summary>Returns true when the next attempt should start over with /hello.</summary>
        private async Task<bool> HandleApiErrorAsync(ApiException e)
        {
            string error = e.Error ?? "http_" + e.Status;
            switch (error)
            {
                case "invalid_key":
                    Fatal(ClientState.StoppedInvalidKey, "Ticket Tool doesn't recognize key " + Key
                        + ". It was revoked, rotated or mistyped: paste the current key from the dashboard and reload.");
                    return false;
                case "wrong_plugin_type":
                    object expected;
                    e.Body.TryGetValue("expected", out expected);
                    Fatal(ClientState.StoppedError, "Key " + Key + " was made for a different game ("
                        + (expected ?? "?") + "). Create a " + _plugin.Type + " key on the dashboard.");
                    return false;
                case "unsupported_protocol":
                    Fatal(ClientState.StoppedError,
                        "Ticket Tool no longer serves protocol " + ProtocolVersion + ". Update the plugin.");
                    return false;
                case "plan_not_entitled":
                    _state = ClientState.PausedPlan;
                    Note("The Discord server's plan doesn't include game lookups. Checking again in an hour.", null);
                    await PauseAsync(PlanRetry).ConfigureAwait(false);
                    return true;
                case "rate_limited":
                    await FailedAsync("Ticket Tool is rate limiting this key; waiting a minute", null,
                        RateLimitWait + Jitter(5000)).ConfigureAwait(false);
                    return false;
                case "too_many_polls":
                    TimeSpan wait = _backoff.Next();
                    await FailedAsync("Three polls are already open for key " + Key
                                      + ". Is the same key in use on another server? Each server needs its own key.",
                        null, wait < TooManyPollsMinWait ? TooManyPollsMinWait + Jitter(5000) : wait)
                        .ConfigureAwait(false);
                    return false;
                case "bad_signature":
                case "missing_headers":
                case "bad_nonce":
                    await FailedAsync("Ticket Tool rejected the request signature (" + error
                                      + "). Check the key was pasted whole.", null, _backoff.Next())
                        .ConfigureAwait(false);
                    return false;
                default:
                    await FailedAsync("Ticket Tool returned " + error + " (HTTP " + e.Status + ")", null,
                        _backoff.Next()).ConfigureAwait(false);
                    return false;
            }
        }

        private void Dispatch(LookupRequest request)
        {
            if (request.Type == ActionRequest.TypePlayerAction) return;
            if (request.Type != LookupRequest.TypePlayerLookup)
            {
                _log.Debug("Ignoring request type " + request.Type);
                return;
            }
            DateTimeOffset received = ServerNow();
            if (request.ExpiresAt <= received)
            {
                _log.Debug("Dropping expired request " + request.Id);
                return;
            }
            Task.Run(() => AnswerAsync(request, received));
        }

        private async Task AnswerAsync(LookupRequest request, DateTimeOffset received)
        {
            string identityField = Messages.IdentityField(_plugin.Type);
            if (request.Include.Count == 0
                || (identityField != null && Messages.RequestIdentity(request, identityField) == null))
            {
                await RespondAsync(request, LookupResult.Failure(LookupResult.Unsupported)).ConfigureAwait(false);
                return;
            }
            DateTimeOffset budgetEnd = received + _answerBudget;
            DateTimeOffset deadline = budgetEnd < request.ExpiresAt ? budgetEnd : request.ExpiresAt;
            TimeSpan remaining = deadline - received;
            if (remaining < TimeSpan.FromMilliseconds(1)) remaining = TimeSpan.FromMilliseconds(1);

            LookupResult result;
            using (var cts = new CancellationTokenSource())
            {
                Task<LookupResult> work;
                try
                {
                    work = _handler(request, cts.Token) ?? Task.FromResult<LookupResult>(null);
                }
                catch (Exception e)
                {
                    work = FromException(e);
                }
                Task finished = await Task.WhenAny(work, Task.Delay(remaining)).ConfigureAwait(false);
                if (finished != work)
                {
                    cts.Cancel();
                    result = LookupResult.Failure(LookupResult.Error, "The game server took too long to answer");
                }
                else if (work.IsFaulted || work.IsCanceled)
                {
                    _log.Warn("Lookup " + request.Id + " failed", work.Exception == null ? null : work.Exception.GetBaseException());
                    result = LookupResult.Failure(LookupResult.Error, "The plugin hit an error answering this lookup");
                }
                else
                {
                    result = work.Result ?? LookupResult.Failure(LookupResult.Error, "The plugin had no answer");
                }
            }
            if (ServerNow() >= request.ExpiresAt)
            {
                _log.Debug("Answer for " + request.Id + " is too late; not sending it");
                return;
            }
            await RespondAsync(request, result).ConfigureAwait(false);
        }

        private void DispatchAction(ActionRequest request)
        {
            DateTimeOffset received = ServerNow();
            if (request.ExpiresAt <= received)
            {
                _log.Debug("Dropping expired action " + request.Id);
                return;
            }
            Task.Run(() => ActAsync(request, received));
        }

        private async Task ActAsync(ActionRequest request, DateTimeOffset received)
        {
            if (_actionHandler == null || !_actions().Contains(request.Kind))
            {
                _log.Info("Refused a " + request.Kind + " from " + request.ActorName
                          + ": that action is off in the plugin's config");
                await RespondActionAsync(request, ActionResult.Failure(ActionResult.NotAllowed)).ConfigureAwait(false);
                return;
            }
            string identityField = Messages.IdentityField(_plugin.Type);
            if (identityField != null && Messages.PlayerIdentity(request.Player, identityField) == null)
            {
                await RespondActionAsync(request, ActionResult.Failure(ActionResult.UnknownPlayer)).ConfigureAwait(false);
                return;
            }
            DateTimeOffset budgetEnd = received + ActionBudget;
            DateTimeOffset deadline = budgetEnd < request.ExpiresAt ? budgetEnd : request.ExpiresAt;
            TimeSpan remaining = deadline - received;
            if (remaining < TimeSpan.FromMilliseconds(1)) remaining = TimeSpan.FromMilliseconds(1);

            ActionResult result;
            using (var cts = new CancellationTokenSource())
            {
                Task<ActionResult> work;
                try
                {
                    work = _actionHandler(request, cts.Token) ?? Task.FromResult<ActionResult>(null);
                }
                catch (Exception e)
                {
                    var tcs = new TaskCompletionSource<ActionResult>();
                    tcs.SetException(e);
                    work = tcs.Task;
                }
                Task finished = await Task.WhenAny(work, Task.Delay(remaining)).ConfigureAwait(false);
                if (finished != work)
                {
                    cts.Cancel();
                    result = ActionResult.Failure(ActionResult.Error, "The game server took too long to carry it out");
                }
                else if (work.IsFaulted || work.IsCanceled)
                {
                    _log.Warn("Action " + request.Id + " failed",
                        work.Exception == null ? null : work.Exception.GetBaseException());
                    result = ActionResult.Failure(ActionResult.Error, "The plugin hit an error carrying out this action");
                }
                else
                {
                    result = work.Result ?? ActionResult.Failure(ActionResult.Error, "The plugin had no answer");
                }
            }
            // Unlike a lookup, a late action already happened in game, so staff still hear about it.
            await RespondActionAsync(request, result).ConfigureAwait(false);
        }

        private async Task RespondActionAsync(ActionRequest request, ActionResult result)
        {
            try
            {
                await _transport.CallAsync("/respond", Messages.RespondAction(request, result), CallTimeout,
                    _stopping.Token).ConfigureAwait(false);
                Interlocked.Increment(ref _answered);
            }
            catch (ApiException e)
            {
                if (e.Error == "unknown_request")
                    _log.Debug("Action " + request.Id + " expired before the answer arrived");
                else
                    _log.Warn("Ticket Tool refused the result of action " + request.Id + ": " + e.Message, null);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _log.Warn("Couldn't send the result of action " + request.Id + ": " + RootMessage(e), null);
            }
        }

        private static Task<LookupResult> FromException(Exception e)
        {
            var tcs = new TaskCompletionSource<LookupResult>();
            tcs.SetException(e);
            return tcs.Task;
        }

        private async Task RespondAsync(LookupRequest request, LookupResult result)
        {
            try
            {
                await _transport.CallAsync("/respond", Messages.Respond(request, result, _plugin.Type), CallTimeout,
                    _stopping.Token).ConfigureAwait(false);
                Interlocked.Increment(ref _answered);
            }
            catch (ApiException e)
            {
                if (e.Error == "unknown_request")
                    _log.Debug("Request " + request.Id + " expired before the answer arrived");
                else
                    _log.Warn("Ticket Tool refused the answer to " + request.Id + ": " + e.Message, null);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _log.Warn("Couldn't send the answer to " + request.Id + ": " + RootMessage(e), null);
            }
        }

        private DateTimeOffset ServerNow()
        {
            return _clock() + TimeSpan.FromSeconds(_transport.OffsetSeconds);
        }

        private void Recovered()
        {
            if (_failureStreak > 0) _log.Info("Reconnected to Ticket Tool");
            _failureStreak = 0;
            _backoff.Reset();
        }

        // Logs the first failure of a streak as a warning and the rest at debug, then waits.
        private async Task FailedAsync(string message, Exception error, TimeSpan wait)
        {
            _state = ClientState.Reconnecting;
            Note(message, error is IOException || error is TimeoutException ? null : error);
            _log.Debug("Retrying in " + (long)wait.TotalMilliseconds + " ms");
            await PauseAsync(wait).ConfigureAwait(false);
        }

        private void Note(string message, Exception error)
        {
            _lastError = message;
            _lastErrorAt = _clock();
            if (_failureStreak++ == 0) _log.Warn(message, error);
            else _log.Debug(message);
        }

        private void Fatal(ClientState stopState, string message)
        {
            _lastError = message;
            _lastErrorAt = _clock();
            _state = stopState;
            _running = false;
            _log.Warn(message, null);
        }

        private async Task PauseAsync(TimeSpan wait)
        {
            try
            {
                await _delay(wait, _stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _running = false;
            }
        }

        private TimeSpan Jitter(int maxMillis)
        {
            lock (_randomLock) return TimeSpan.FromMilliseconds(_random.Next(maxMillis));
        }

        private static string RootMessage(Exception e)
        {
            Exception t = e;
            var agg = t as AggregateException;
            if (agg != null) t = agg.GetBaseException();
            while (t.InnerException != null && t.InnerException != t) t = t.InnerException;
            return string.IsNullOrWhiteSpace(t.Message) ? t.GetType().Name : t.Message;
        }
    }
}
