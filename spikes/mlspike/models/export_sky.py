"""Export local ADE20K DeepLab frozen graph or OneFormer directory."""

import argparse
from pathlib import Path

from export_common import config, evidence, file_hash, new_output, verify_weights


def deeplab(args, output):
    import tensorflow as tf
    import tf2onnx
    verify_weights(args.source, args.source_sha256)
    graph = tf.compat.v1.GraphDef()
    graph.ParseFromString(args.source.read_bytes())
    # The 2018 graph carries static _output_shapes from its training crop; they conflict
    # with the decoder's real shapes in ONNX (Concat 128 vs 129), so let TF re-infer them.
    for node in graph.node:
        if "_output_shapes" in node.attr:
            del node.attr["_output_shapes"]
    # Dynamic spatial dims: a static input lets tf2onnx fold a decoder resize to 128 while
    # the skip branch is 129, which ONNX Runtime rejects. The harness still feeds --size.
    spec = tf.TensorSpec([1, None, None, 3], tf.float32, name="image")

    @tf.function(input_signature=[spec])
    def inference(image):
        result = tf.import_graph_def(
            graph, input_map={args.input_tensor: tf.cast(image * 255.0, tf.uint8)},
            return_elements=[args.output_tensor], name="")[0]
        if args.output_kind == "logits":
            # The 2018 ADE20K mobile graph exposes upsampled logits, not probabilities.
            result = tf.nn.softmax(result, axis=-1)
        return tf.identity(result, name="mask")

    tf2onnx.convert.from_function(inference, input_signature=[spec], opset=17,
                                  output_path=str(output))
    import onnx
    exported = onnx.load(str(output))
    settings = config("deeplab-mobile-ade20k", args.size, "sky")
    settings.update(input_layout="NHWC", output_layout="NHWC", mean=[0, 0, 0],
                    std=[1, 1, 1], class_index=args.sky_class,
                    input_name=exported.graph.input[0].name, output_name=exported.graph.output[0].name)
    return settings, {"source_sha256": args.source_sha256}


def oneformer(args, output):
    import json
    import torch
    from transformers import OneFormerForUniversalSegmentation, OneFormerProcessor
    # Host supplies hashes for every file in the pinned HF snapshot.
    hashes = json.loads(args.source_hashes.read_text(encoding="utf-8"))
    for name, digest in hashes.items():
        path = (args.source / name).resolve()
        if args.source.resolve() not in path.parents:
            raise ValueError("Snapshot hash path escapes source")
        verify_weights(path, digest)
    actual = {str(p.relative_to(args.source)).replace("\\", "/")
              for p in args.source.rglob("*") if p.is_file()}
    if actual != set(hashes):
        raise ValueError("Snapshot file inventory differs from supplied hashes")
    model = OneFormerForUniversalSegmentation.from_pretrained(args.source, local_files_only=True).eval()
    processor = OneFormerProcessor.from_pretrained(args.source, local_files_only=True)
    label = model.config.id2label.get(args.sky_class, "")
    if "sky" not in label.lower():
        raise ValueError(f"Selected class is not sky: {label}")
    task = processor.tokenizer(
        ["the task is semantic"], padding="max_length", max_length=model.config.task_seq_len,
        truncation=True, return_tensors="pt")["input_ids"]

    class Sky(torch.nn.Module):
        def __init__(self):
            super().__init__()
            self.model = model
            self.register_buffer("task", task)

        def forward(self, image):
            prediction = self.model(pixel_values=image, task_inputs=self.task)
            classes = prediction.class_queries_logits.softmax(-1)[..., :-1]
            masks = prediction.masks_queries_logits.sigmoid()
            scores = torch.einsum("bqc,bqhw->bchw", classes, masks)
            probabilities = scores / scores.sum(dim=1, keepdim=True).clamp_min(1e-8)
            return probabilities[:, args.sky_class:args.sky_class + 1]

    torch.onnx.export(Sky().eval(), torch.zeros(1, 3, args.size, args.size), str(output),
                      input_names=["image"], output_names=["mask"], opset_version=17, dynamo=False)
    settings = config("oneformer-swin-t-ade20k", args.size, "sky")
    settings.update(mean=processor.image_processor.image_mean, std=processor.image_processor.image_std)
    return settings, {"source_files": hashes, "source_hashes_sha256": file_hash(args.source_hashes),
                      "semantic_probabilities": "query-weighted class scores normalized over classes"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate", choices=["deeplab", "oneformer"], required=True)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--source-sha256")
    parser.add_argument("--source-hashes", type=Path)
    parser.add_argument("--sky-class", type=int, required=True)
    parser.add_argument("--size", type=int, required=True)
    parser.add_argument("--input-tensor", default="ImageTensor:0")
    parser.add_argument("--output-tensor", default="SemanticProbabilities:0")
    parser.add_argument("--output-kind", choices=["probabilities", "logits"], default="probabilities",
                        help="logits: apply a class softmax before exporting (DeepLab ResizeBilinear_2:0)")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.size < 1 or args.sky_class < 0:
        parser.error("size must be positive and sky class nonnegative")
    if args.candidate == "deeplab" and not args.source_sha256:
        parser.error("--source-sha256 is required for the extracted frozen graph")
    if args.candidate == "oneformer" and not args.source_hashes:
        parser.error("--source-hashes is required for the local HF snapshot")
    output = new_output(args.output)
    settings, extra = deeplab(args, output) if args.candidate == "deeplab" else oneformer(args, output)
    import onnx
    onnx.checker.check_model(str(output))
    evidence(output, args, settings, extra)


if __name__ == "__main__":
    main()
