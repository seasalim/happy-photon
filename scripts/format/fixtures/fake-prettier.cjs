// Stand-in for a broken Prettier install, used only by qualify-format.ps1.
const mode = process.env.HP_FAKE_PRETTIER ?? "version";
const args = process.argv.slice(2);
if (args.includes("--version")) {
  process.stdout.write(mode === "version" ? "0.0.0\n" : "3.9.9\n");
} else if (mode === "crash" || (mode === "write-crash" && args.includes("--write"))) {
  process.stderr.write("[error] fake formatter crash\n");
  process.exitCode = 1;
}
