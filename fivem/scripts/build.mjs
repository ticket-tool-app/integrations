// Bundles the resource (SDK inlined, so servers need no npm install) and,
// with --package, stages ticket-tool-link/ and zips it for a release.
import { execFileSync } from "node:child_process";
import { cpSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { build } from "esbuild";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const args = process.argv.slice(2);
const pkg = JSON.parse(readFileSync(join(root, "package.json"), "utf8"));
const versionArg = args.indexOf("--version");
const version = versionArg >= 0 ? args[versionArg + 1] : pkg.version;
if (!/^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$/.test(version ?? "")) {
  console.error(`Version "${version}" isn't MAJOR.MINOR.PATCH`);
  process.exit(1);
}

const resourceDir = join(root, "ticket-tool-link");
await build({
  entryPoints: [join(root, "src/server.ts")],
  outfile: join(resourceDir, "dist/server.js"),
  bundle: true,
  // FXServer runs server scripts as plain scripts, not CommonJS modules.
  format: "iife",
  platform: "node",
  // FXServer's default Node runtime is 16.
  target: "node16",
  tsconfig: join(root, "tsconfig.json"),
  legalComments: "none",
  banner: { js: `// Ticket Tool Link ${version} for FiveM and RedM. MIT License, JTF Labs Pty Ltd.` },
  logLevel: "info",
});

const manifestPath = join(resourceDir, "fxmanifest.lua");
const manifest = readFileSync(manifestPath, "utf8").replace(/^version '.*'$/m, `version '${version}'`);

if (args.includes("--package")) {
  const out = join(root, "build");
  const stage = join(out, "ticket-tool-link");
  rmSync(out, { recursive: true, force: true });
  mkdirSync(join(stage, "dist"), { recursive: true });
  writeFileSync(join(stage, "fxmanifest.lua"), manifest);
  cpSync(join(resourceDir, "dist/server.js"), join(stage, "dist/server.js"));
  cpSync(join(resourceDir, "config.example.json"), join(stage, "config.example.json"));
  cpSync(join(root, "../LICENSE"), join(stage, "LICENSE"));
  const zip = `ticket-tool-link-fivem-${version}.zip`;
  execFileSync("zip", ["-r", "-X", zip, "ticket-tool-link"], { cwd: out, stdio: "inherit" });
  console.log(`Packaged build/${zip}`);
}
