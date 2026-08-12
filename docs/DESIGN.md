---
name: Happy Photon
colors:
  surface: '#131318'
  surface-dim: '#131318'
  surface-bright: '#39383e'
  surface-container-lowest: '#0e0e13'
  surface-container-low: '#1b1b20'
  surface-container: '#1f1f25'
  surface-container-high: '#2a292f'
  surface-container-highest: '#35343a'
  on-surface: '#e4e1e9'
  on-surface-variant: '#b9cacb'
  inverse-surface: '#e4e1e9'
  inverse-on-surface: '#303036'
  outline: '#849495'
  outline-variant: '#3b494b'
  surface-tint: '#00dbe9'
  primary: '#dbfcff'
  on-primary: '#00363a'
  primary-container: '#00f0ff'
  on-primary-container: '#006970'
  inverse-primary: '#006970'
  secondary: '#fface8'
  on-secondary: '#5e0053'
  secondary-container: '#ff24e4'
  on-secondary-container: '#520049'
  tertiary: '#faf3ff'
  on-tertiary: '#3c0090'
  tertiary-container: '#e1d2ff'
  on-tertiary-container: '#7213ff'
  white-balance-cool: '#74f7ff'
  white-balance-neutral: '#9df7b0'
  white-balance-warm: '#f4ff69'
  white-balance-tint-green: '#73b95a'
  white-balance-tint-magenta: '#ec3c7e'
  error: '#ffb4ab'
  on-error: '#690005'
  error-container: '#93000a'
  on-error-container: '#ffdad6'
  primary-fixed: '#7df4ff'
  primary-fixed-dim: '#00dbe9'
  on-primary-fixed: '#002022'
  on-primary-fixed-variant: '#004f54'
  secondary-fixed: '#ffd7f0'
  secondary-fixed-dim: '#fface8'
  on-secondary-fixed: '#3a0033'
  on-secondary-fixed-variant: '#840076'
  tertiary-fixed: '#e9ddff'
  tertiary-fixed-dim: '#d1bcff'
  on-tertiary-fixed: '#23005b'
  on-tertiary-fixed-variant: '#5700c9'
  background: '#131318'
  on-background: '#e4e1e9'
  surface-variant: '#35343a'
typography:
  display-lg:
    fontFamily: Sora
    fontSize: 72px
    fontWeight: '800'
    lineHeight: 80px
    letterSpacing: -0.04em
  headline-lg:
    fontFamily: Sora
    fontSize: 48px
    fontWeight: '700'
    lineHeight: 56px
    letterSpacing: -0.02em
  headline-lg-mobile:
    fontFamily: Sora
    fontSize: 32px
    fontWeight: '700'
    lineHeight: 40px
    letterSpacing: -0.02em
  headline-md:
    fontFamily: Sora
    fontSize: 32px
    fontWeight: '600'
    lineHeight: 40px
  body-lg:
    fontFamily: Hanken Grotesk
    fontSize: 18px
    fontWeight: '400'
    lineHeight: 28px
  body-md:
    fontFamily: Hanken Grotesk
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  label-md:
    fontFamily: JetBrains Mono
    fontSize: 14px
    fontWeight: '500'
    lineHeight: 20px
    letterSpacing: 0.05em
  label-sm:
    fontFamily: JetBrains Mono
    fontSize: 12px
    fontWeight: '500'
    lineHeight: 16px
    letterSpacing: 0.08em
rounded:
  sm: 0.25rem
  DEFAULT: 0.5rem
  md: 0.75rem
  lg: 1rem
  xl: 1.5rem
  full: 9999px
spacing:
  base: 8px
  xs: 4px
  sm: 12px
  md: 24px
  lg: 48px
  xl: 80px
  gutter: 24px
  margin-mobile: 16px
  margin-desktop: 64px
---

## Brand & Style

The brand personality for the design system is energetic, luminous, and high-velocity. It targets a tech-forward audience that values performance and visual stimulation. The UI should evoke a sense of "captured light"—vibrant, focused, and humming with energy.

The aesthetic follows a **Vivid Chroma** style: a dark-mode foundation where components appear as light-emitting objects rather than static surfaces. It blends elements of **Glassmorphism** (for depth and light refraction) with **Modern Corporate** precision. Every interaction should feel like a burst of photons—instant, bright, and purposeful.

## Colors

The palette is anchored in deep space blacks to allow the "photons" to pop.
- **Primary (Electric Cyan):** Used for core actions and active states. It represents pure light energy.
- **Secondary (Neon Magenta):** Used for accents, highlights, ratings, and secondary interactions.
- **Tertiary (Proton Purple):** Used for deep gradients, subtle background glows, and passive edit badges.
- **White balance spectrum:** The Kelvin and tint tracks use dedicated cyan→green→yellow and green→magenta functional gradients so their direction is readable at a glance.
- **Neutral:** A range of ultra-dark navys and blacks (`#0A0A0F` to `#1A1A24`) to provide a high-contrast canvas for the vivid accents.

Avoid muddy colors. Use high-saturation tones and implement luminosity masks to ensure the neon hues feel integrated into the dark environment.

### Application themes

Dark is the default. Mid-grey is the second persistent theme and keeps the same
cyan, magenta, and semantic accents while raising the neutral chrome. Its photograph
surround is `#777777`, the nearest integer sRGB encoding of CIE L\* 50. That code
value is about 47% of the encoded channel range but decodes to roughly 18.4% relative
luminance because sRGB is nonlinear. The familiar 18% photographic grey describes a
physical reflectance convention whose displayed appearance also depends on lighting
and color management; Happy Photon therefore targets the display-referred L\* 50
reference and documents its close relationship to 18% grey rather than treating a
reflectance card as the implementation value.

Mid-grey remains a dark-family theme with light text. Its chrome ramp stays below the
surround, and text is placed on darker cards rather than directly on `#777777`.
`AssessmentGrey` uses the same shipped value but is an invariant assessment reference,
not an alias to the theme surround. `AssessmentWhite` is the invariant `#FFFFFF`
reference band used with it. Theme resources live in
`Themes/HappyPhotonTheme.axaml`; code-drawn photograph overlays use the matching
invariant values in `Views/HappyPhotonColors.cs`.

The asset audit found no bitmap that depends on a dark backing. The title-bar icon is
self-contained; all other interface marks are text or vector paths and inherit theme
resources.

## Typography

Typography in this design system emphasizes a technical yet premium feel. 
- **Headlines:** Sora provides a geometric, futuristic weight that feels bold and innovative. Use "Display" sizes for hero sections with tight letter spacing to mimic high-end editorial tech layouts.
- **Body:** Hanken Grotesk offers high legibility and a contemporary edge for long-form content and UI descriptions.
- **Labels/Data:** JetBrains Mono is utilized for small metadata, tags, and "technical" specs to reinforce the "Photon" precision theme. 

The desktop welcome surface uses named display tokens rather than local sizes:
`FontSizeHero` (48px) for the cyan welcome wordmark and `FontSizeFeature` (34px)
for the two-tone onboarding headline.

Scale typography aggressively on mobile; headlines should shrink significantly while body text remains legible at 16px.

## Layout & Spacing

The layout philosophy follows a **Fluid Grid** model with high-impact margins. 
- **Desktop:** 12-column grid with wide 64px outer margins to create a "cinematic" feel. Gutters are fixed at 24px to maintain structural tension.
- **Mobile:** 4-column grid with 16px margins. Content should be edge-to-edge for immersive visuals (e.g., cards and images).

Spacing rhythm is strictly based on an 8px scale. Use large `xl` (80px+) vertical spacing between sections to allow the dark background to "breathe" and create a sense of vastness.

## Elevation & Depth

Elevation is communicated through **Light Emission** rather than physical shadow.
- **Tonal Layers:** Surfaces further from the "floor" are lighter in tone. The base is `#0A0A0F`, containers are `#16161E`, and floating elements are `#22222E`.
- **Glows:** Instead of black shadows, use "Photon Glows"—subtle, high-blur outer shadows tinted with the primary or secondary color (e.g., 20% opacity Electric Cyan).
- **Glassmorphism:** Use backdrop-blur (20px+) on overlays and navigation bars to simulate light passing through high-density energy fields.

## Shapes

The shape language is "Squircle-adjacent"—sophisticated and intentional. 
- **Standard:** Use `0.5rem` (8px) for buttons and input fields to keep them feeling precise.
- **Large Containers:** Use `1.5rem` (24px) for cards and modals to soften the high-contrast aesthetic.
- **Interactive Triggers:** Some small decorative elements may use pill-shapes to indicate "capsules" of energy.

## Components

Color labels use fixed red, yellow, green, blue, and purple visual slots. Library
thumbnails show the assigned color as a round marker in the lower-right corner, kept
clear of the burst stripe on the left edge and the online-only badge above it. The
assessment and filter controls generate their swatches from the append-only label enum;
the filter row shows swatches alone and carries each name in its tooltip.

- **Buttons:** Primary buttons should feature a subtle inner glow and a soft drop-shadow in the primary color. On hover, the luminosity increases.
- **Input Fields:** Use a "Ghost" style—thin 1px borders in a muted neutral, turning to Electric Cyan on focus with a faint outer glow.
- **Cards:** Incorporate a subtle top-down gradient stroke (1px) to catch the "light" from above. Backgrounds should use a semi-transparent dark tint with backdrop-blur.
- **Chips:** Monospaced labels inside pill-shaped containers with high-saturation borders.
- **Data Visualization:** Use "Photon Trails"—thin, glowing lines with gradient tails to represent motion and data flow.
- **Progress Indicators:** Linear bars with a "pulse" animation, moving from Secondary to Primary color to represent energy charging.
