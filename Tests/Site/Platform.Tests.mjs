import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const platformSource = readFileSync(new URL("../../site/assets/js/platform.js", import.meta.url), "utf8");
const platformModule = await import(`data:text/javascript;base64,${Buffer.from(platformSource).toString("base64")}`);
const { classifyPlatform, detectPlatform } = platformModule;

test("classifies supported desktop platforms", () => {
  assert.equal(classifyPlatform("Windows"), "windows");
  assert.equal(classifyPlatform("MacIntel"), "macos");
  assert.equal(classifyPlatform("Linux x86_64"), "linux");
});

test("keeps mobile and ChromeOS neutral", () => {
  assert.equal(classifyPlatform("Android"), "unknown");
  assert.equal(classifyPlatform("iPhone"), "unknown");
  assert.equal(classifyPlatform("CrOS x86_64"), "unknown");
});

test("prefers userAgentData without attempting architecture detection", () => {
  const environment = {
    userAgentData: { platform: "macOS" },
    platform: "Win32",
    userAgent: "Windows"
  };
  assert.equal(detectPlatform(environment), "macos");
});

test("falls back conservatively", () => {
  assert.equal(detectPlatform({ platform: "Win32" }), "windows");
  assert.equal(detectPlatform({ userAgent: "X11; Linux x86_64" }), "linux");
  assert.equal(detectPlatform({ userAgent: "Mozilla/5.0 (iPhone)" }), "unknown");
  assert.equal(detectPlatform({}), "unknown");
});
