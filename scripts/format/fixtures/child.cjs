// Child processes for the bounded-runner controls in qualify-format.ps1.
const [mode, marker] = process.argv.slice(2);
if (mode === "hang") {
  setInterval(() => {}, 1000, marker);
} else if (mode === "noisy") {
  process.stdout.write("x".repeat(200000));
}
