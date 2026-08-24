# Render v11 geometry attribution

Render v11 replaces the horizon rotate/crop sequence with the fused corrected-frame
warp. The 47 inherited cases contain no horizon or manual geometry. Forty-four
regenerated files are byte-identical to v10; the three Fujifilm X30 files retain their
documented native-demosaic run-to-run variance. Sixteen additive baselines on the
canonical sRGB fixture exercise Vertical, Horizontal, Aspect, and Distortion
independently at −100, −50, +50, and +100.

The render-version increment is required because existing images with horizon rotation
now use the unified blank-free corrected frame. No stored crop is migrated.
