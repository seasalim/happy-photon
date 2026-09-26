"""Store selected float32 weights as float16 and cast them back to float32 at load.

Compute stays float32 (the graph sees the same values up to fp16 rounding); only the
download shrinks. Used for OneFormer's Swin backbone, which cannot take int8 weights,
because both fp16 graph converters produce invalid models for it.
"""

import argparse
import json
from pathlib import Path

from export_common import evidence, file_hash, new_output, verify_weights


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--prefix", required=True, help="node-name prefix whose weights are stored as fp16")
    parser.add_argument("--min-elements", type=int, default=1024)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    settings = json.loads(args.config.read_text(encoding="utf-8"))
    verify_weights(args.model, settings["model_sha256"])
    output = new_output(args.output)

    import numpy as np
    import onnx
    from onnx import TensorProto, helper, numpy_helper

    model = onnx.load(str(args.model))
    graph = model.graph
    consumers = {}
    for node in graph.node:
        for name in node.input:
            consumers.setdefault(name, []).append(node.name)
    kept, casts, converted = [], [], 0
    for tensor in graph.initializer:
        users = consumers.get(tensor.name, [])
        size = int(np.prod(tensor.dims)) if tensor.dims else 1
        if (tensor.data_type == TensorProto.FLOAT and size >= args.min_elements and users
                and all(u.startswith(args.prefix) for u in users)):
            half = numpy_helper.from_array(numpy_helper.to_array(tensor).astype(np.float16),
                                           tensor.name + "__fp16")
            kept.append(half)
            casts.append(helper.make_node("Cast", [half.name], [tensor.name],
                                          to=TensorProto.FLOAT, name=tensor.name + "__cast"))
            converted += 1
        else:
            kept.append(tensor)
    del graph.initializer[:]
    graph.initializer.extend(kept)
    nodes = casts + list(graph.node)
    del graph.node[:]
    graph.node.extend(nodes)
    onnx.checker.check_model(model)
    onnx.save(model, str(output))
    evidence(output, args, settings, {
        "source_sha256": file_hash(args.model), "source_config_sha256": file_hash(args.config),
        "calibration": f"none; {converted} float32 weights under {args.prefix} stored as float16, cast to float32 at load",
    })


if __name__ == "__main__":
    main()
