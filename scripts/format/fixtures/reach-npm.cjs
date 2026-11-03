// Network probe for the formatter qualification workflow: exits 0 when the npm registry answers, 3 otherwise.
fetch("https://registry.npmjs.org/").then(
  () => process.exit(0),
  () => process.exit(3),
);
