/** A reply with `ok: false` or a non-2xx status. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    /** The protocol error code, e.g. `invalid_key`; null when the reply wasn't JSON. */
    readonly error: string | null,
    readonly body: Record<string, unknown> = {},
  ) {
    super(`HTTP ${status}${error ? " " + error : ""}`);
    this.name = "ApiError";
  }
}

/** The reply wasn't provably from Ticket Tool: bad signature, nonce or timestamp, or unsigned success. */
export class ReplyVerificationError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "ReplyVerificationError";
  }
}

export class HttpTimeoutError extends Error {
  constructor(message = "Request timed out") {
    super(message);
    this.name = "HttpTimeoutError";
  }
}

export class AbortedError extends Error {
  constructor() {
    super("Request aborted");
    this.name = "AbortedError";
  }
}
