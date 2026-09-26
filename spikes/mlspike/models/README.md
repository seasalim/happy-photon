# Local candidate conversion

Claude owns downloads, licence verification, pinned source checkouts and actual
conversion runs. These scripts never download weights or source, and refuse outputs
inside the product repo. Use Python 3.10/3.11; install the pinned requirements in
separate subject/OneFormer and legacy TensorFlow virtual environments.

Subject export accepts --candidate birefnet-lite|isnet|u2netp, --source (clean
upstream Git checkout), --source-revision (full commit), --weights,
--weights-sha256, and --output. Sizes are 1024/1024/320. BiRefNet selects the lite
Swin-T architecture and matching decoder channels, then strictly loads safetensors.
IS-Net and U2-Net-p strictly load weights-only checkpoints. Their selected output
is already sigmoid; BiRefNet applies sigmoid to its last output. No frozen image
is loaded. Source repositories and dependency versions must be kept with evidence.

Sky export accepts --candidate deeplab|oneformer, --source, --size,
--sky-class and --output. For DeepLab, source is the extracted frozen GraphDef,
with --source-sha256. Default tensors are ImageTensor:0 and
SemanticProbabilities:0; inspect the pinned graph and pass --input-tensor and
--output-tensor if its exported probability output uses different names.
A labels/argmax tensor is not a substitute for probabilities. The wrapper maps
float RGB [0,1] to the graph's uint8 RGB input. Use the graph's ADE20K class map
(including background offset) when selecting sky.

For OneFormer, source is a local Hugging Face snapshot and --source-hashes is a
JSON map of every relative file to its SHA-256. The model must identify the selected
class as sky. The semantic task is baked into the graph. Query-mask/class scores
are summed and normalized across classes, then the sky probability is emitted.
Mask2Former was rejected at S1 in Claude's licence evidence and has no exporter.

quantize.py takes --model, --config, --format fp16|int8 and --output.
FP16 retains float32 I/O. INT8 uses QDQ with per-channel weights and eight fixed,
seed-51 synthetic RGB calibration frames. It never calibrates or tunes against the
frozen evaluation set. Synthetic calibration may degrade accuracy; measure it as
a distinct candidate. Neither export success nor ONNX checker success establishes
CPU kernel support, numerical agreement, size compliance or a quality pass.

Each conversion emits .onnx, adjacent .json harness config, and .evidence.json
with source arguments, hashes, byte size and calibration policy. Preserve all three
outside Git. Stage the chosen pair as model.onnx/model.json (or subject/sky pairs)
for the packaged probe. Run a host CPU smoke inference before collecting gates.
