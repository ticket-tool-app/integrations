#!/usr/bin/env python3
"""Builds the single-file Oxide/Carbon plugin: rust/src/TicketToolLink.cs with
the SDK from sdk-dotnet appended, since Oxide only loads one .cs per plugin.

    python3 rust/build.py [version]    ->  rust/dist/TicketToolLink.cs
                                          rust/dist/TicketToolLink-rust-<version>.cs
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SDK = ROOT / "sdk-dotnet" / "src" / "TicketTool.PluginSdk"
SOURCE = ROOT / "rust" / "src" / "TicketToolLink.cs"
DIST = ROOT / "rust" / "dist"

# HttpClient lives in System.Net.Http, which Oxide doesn't reference; the plugin
# uses WebRequestSender instead.
EXCLUDE = {"HttpClientSender.cs"}
VERSION_RE = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$")
USING_RE = re.compile(r"^using [A-Za-z0-9_.]+;\s*$")


def inline(path: pathlib.Path) -> str:
    """Moves a file's using directives inside its namespace block, so they can't
    clash with the plugin's (UnityEngine.Random against System.Random, say)."""
    lines = path.read_text(encoding="utf-8").splitlines()
    usings = [l.strip() for l in lines if USING_RE.match(l)]
    body = [l for l in lines if not USING_RE.match(l)]
    out = []
    placed = False
    for i, line in enumerate(body):
        out.append(line)
        if not placed and line.strip() == "{" and i > 0 and body[i - 1].startswith("namespace "):
            out.extend("    " + u for u in usings)
            placed = True
    if not placed:
        sys.exit(f"{path.name}: expected a 'namespace X' line followed by '{{'")
    if "using System.Net.Http;" in usings:
        sys.exit(f"{path.name} uses System.Net.Http, which Oxide can't compile against")
    return "\n".join(out).strip() + "\n"


def main() -> None:
    version = sys.argv[1] if len(sys.argv) > 1 else "0.0.0-dev"
    if not VERSION_RE.match(version):
        sys.exit(f"Version {version!r} is not MAJOR.MINOR.PATCH[-suffix]")
    # Oxide's [Info] version must be numeric; the full version goes in the header.
    numeric = version.split("-", 1)[0]

    plugin = SOURCE.read_text(encoding="utf-8")
    for old, new in (
        ('[Info("Ticket Tool Link", "Ticket Tool", "0.0.0")]', f'[Info("Ticket Tool Link", "Ticket Tool", "{numeric}")]'),
        ('private const string PluginVersion = "0.0.0-dev";', f'private const string PluginVersion = "{version}";'),
    ):
        if old not in plugin:
            sys.exit(f"Marker not found in {SOURCE.name}: {old}")
        plugin = plugin.replace(old, new)
    if "// @inline-sdk" not in plugin:
        sys.exit("Marker // @inline-sdk not found")

    parts = []
    for path in sorted(SDK.glob("*.cs")):
        if path.name in EXCLUDE:
            continue
        text = inline(path)
        if path.name == "TicketToolClient.cs":
            marker = "return new HttpClientSender(); // @default-sender"
            if marker not in text:
                sys.exit("Default sender marker not found in TicketToolClient.cs")
            text = text.replace(marker, "return new WebRequestSender();")
        parts.append(f"// ---- sdk-dotnet/src/TicketTool.PluginSdk/{path.name}\n{text}")

    code = "\n".join(l for l in "\n".join(parts).splitlines() if not l.lstrip().startswith("//"))
    if re.search(r"\bHttpClient(Sender)?\b", code):
        sys.exit("The inlined SDK still refers to HttpClient")
    sdk = (
        "#region TicketTool.PluginSdk (inlined from sdk-dotnet at build time; edit it there)\n\n"
        + "\n".join(parts)
        + "\n#endregion\n"
    )
    output = plugin.replace("// @inline-sdk", sdk).replace("\r\n", "\n")
    DIST.mkdir(exist_ok=True)
    (DIST / "TicketToolLink.cs").write_text(output, encoding="utf-8")
    (DIST / f"TicketToolLink-rust-{version}.cs").write_text(output, encoding="utf-8")
    print(f"Wrote rust/dist/TicketToolLink-rust-{version}.cs ({len(output.splitlines())} lines)")


if __name__ == "__main__":
    main()
