const platformMatchers = [
  ["windows", /windows|win32/i],
  ["macos", /macos|macintosh|macintel|macppc/i],
  ["linux", /linux|x11/i]
];

const excludedMatchers = /android|iphone|ipad|ipod|cros|mobile/i;

export function classifyPlatform(value = "") {
  if (!value || excludedMatchers.test(value)) {
    return "unknown";
  }

  for (const [platform, matcher] of platformMatchers) {
    if (matcher.test(value)) {
      return platform;
    }
  }

  return "unknown";
}

export function detectPlatform(environment = globalThis.navigator) {
  const highConfidencePlatform = environment?.userAgentData?.platform;
  const legacyPlatform = environment?.platform;
  const userAgent = environment?.userAgent;

  for (const candidate of [highConfidencePlatform, legacyPlatform, userAgent]) {
    const detected = classifyPlatform(candidate);
    if (detected !== "unknown") {
      return detected;
    }
    if (candidate && excludedMatchers.test(candidate)) {
      return "unknown";
    }
  }

  return "unknown";
}
