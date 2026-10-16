// Preloaded with `node -r` to run the bundled dist/server.js outside FXServer.
// Defines the natives it calls as globals, configured from TTL_E2E (JSON), and
// takes commands on stdin: "join <src>", "drop <src>", "cmd <args...>", "stop".
"use strict";
const fs = require("node:fs");
const readline = require("node:readline");

const cfg = JSON.parse(process.env.TTL_E2E || "{}");
const convars = cfg.convars || {};
const resources = Object.assign({ "ticket-tool-link": "started" }, cfg.resources || {});
const players = new Map(Object.entries(cfg.players || {}));
const offline = new Map();
const kvpFile = cfg.kvpFile;
const kvp = new Map(kvpFile && fs.existsSync(kvpFile) ? Object.entries(JSON.parse(fs.readFileSync(kvpFile, "utf8"))) : []);
const handlers = new Map();
const commands = new Map();

const g = globalThis;
g.GetConvar = (n, d) => (n in convars ? convars[n] : d);
g.GetCurrentResourceName = () => "ticket-tool-link";
g.GetResourceState = (r) => resources[r] || "missing";
g.GetResourceMetadata = (_r, k) => (k === "version" ? cfg.version || "1.0.0" : null);
g.LoadResourceFile = (r, f) => {
  const p = (cfg.files || {})[`${r}/${f}`];
  return p ? fs.readFileSync(p, "utf8") : null;
};
g.GetPlayers = () => [...players.keys()];
g.GetPlayerName = (s) => (players.get(String(s)) || offline.get(String(s)) || {}).name || null;
const ids = (s) => (players.get(String(s)) || offline.get(String(s)) || {}).identifiers || [];
g.GetNumPlayerIdentifiers = (s) => ids(s).length;
g.GetPlayerIdentifier = (s, i) => ids(s)[i] || null;
g.SetResourceKvp = (k, v) => {
  kvp.set(k, v);
  if (kvpFile) fs.writeFileSync(kvpFile, JSON.stringify(Object.fromEntries(kvp)));
};
g.GetResourceKvpString = (k) => kvp.get(k) || null;
g.RegisterCommand = (n, h, restricted) => commands.set(n, { h, restricted });
g.on = (e, h) => {
  const list = handlers.get(e) || [];
  list.push(h);
  handlers.set(e, list);
};
g.emitNet = (e, t, ...a) => console.log(`EMITNET ${e} ${t} ${JSON.stringify(a)}`);
g.exports = cfg.exports || {};

function emit(event, source, ...args) {
  g.source = source;
  for (const h of handlers.get(event) || []) h(...args);
}

readline.createInterface({ input: process.stdin }).on("line", (line) => {
  const [word, ...rest] = line.trim().split(/\s+/);
  if (word === "join") {
    const p = offline.get(rest[0]);
    if (p) players.set(rest[0], p);
    emit("playerJoining", rest[0], "0");
  } else if (word === "drop") {
    const p = players.get(rest[0]);
    emit("playerDropped", rest[0], "Exiting");
    players.delete(rest[0]);
    if (p) offline.set(rest[0], p);
  } else if (word === "cmd") {
    const c = commands.get("ticketlink");
    console.log(`COMMAND restricted=${c && c.restricted}`);
    c.h(0, rest, `ticketlink ${rest.join(" ")}`);
  } else if (word === "ban") {
    emit("txAdmin:events:playerBanned", "", JSON.parse(rest.join(" ")));
  } else if (word === "stop") {
    emit("onResourceStop", "", "ticket-tool-link");
    setTimeout(() => process.exit(0), 2500);
  }
});
