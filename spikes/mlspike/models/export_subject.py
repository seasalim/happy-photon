"""Export the three pinned subject candidates from local source and weights."""

import argparse
import importlib
from pathlib import Path
import sys

from export_common import config, evidence, new_output, verify_source, verify_weights


def load_model(args):
    import torch
    verify_source(args.source, args.source_revision)
    verify_weights(args.weights, args.weights_sha256)
    sys.path.insert(0, str(args.source.resolve()))
    if args.candidate == "birefnet-lite":
        # Lite is an architecture choice, never a learned/frozen-set adjustment.
        configuration = importlib.import_module("config")
        original = configuration.Config.__init__

        def lite_init(self, *values, **keywords):
            original(self, *values, **keywords)
            self.bb = "swin_v1_t"
            channels = [768, 384, 192, 96]
            if self.mul_scl_ipt == "cat":
                channels = [value * 2 for value in channels]
            self.lateral_channels_in_collection = channels
            self.cxt = channels[1:][::-1][-self.cxt_num:] if self.cxt_num else []

        configuration.Config.__init__ = lite_init
        model = importlib.import_module("models.birefnet").BiRefNet(bb_pretrained=False)
        from safetensors.torch import load_file
        weights = load_file(str(args.weights), device="cpu")
    elif args.candidate == "isnet":
        sys.path.insert(0, str((args.source / "IS-Net").resolve()))
        model = importlib.import_module("models.isnet").ISNetDIS()
        weights = torch.load(args.weights, map_location="cpu", weights_only=True)
    else:
        model = importlib.import_module("model.u2net").U2NETP(3, 1)
        weights = torch.load(args.weights, map_location="cpu", weights_only=True)
    model.load_state_dict({key.removeprefix("module."): value for key, value in weights.items()}, strict=True)
    model.eval()
    return model


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate", choices=["birefnet-lite", "isnet", "u2netp"], required=True)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--source-revision", required=True)
    parser.add_argument("--weights", type=Path, required=True)
    parser.add_argument("--weights-sha256", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = new_output(args.output)
    import torch
    import onnx
    model = load_model(args)
    size = 320 if args.candidate == "u2netp" else 1024

    class Foreground(torch.nn.Module):
        def __init__(self):
            super().__init__()
            self.model = model

        def forward(self, image):
            predictions = self.model(image)
            if args.candidate == "birefnet-lite":
                return predictions[-1].sigmoid()
            if args.candidate == "isnet":
                return predictions[0][0]
            return predictions[0]

    torch.onnx.export(Foreground().eval(), torch.zeros(1, 3, size, size), str(output),
                      input_names=["image"], output_names=["mask"], opset_version=17,
                      dynamo=False)
    onnx.checker.check_model(str(output))
    evidence(output, args, config(args.candidate, size))


if __name__ == "__main__":
    main()
