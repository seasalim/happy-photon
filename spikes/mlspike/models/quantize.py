"""Quantize ONNX without observing, training on, or tuning against the frozen set."""

import argparse
import json
from pathlib import Path

from export_common import evidence, file_hash, new_output, verify_weights


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--format", choices=["fp16", "int8", "int8-dynamic"], required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    settings = json.loads(args.config.read_text(encoding="utf-8"))
    verify_weights(args.model, settings["model_sha256"])
    output = new_output(args.output)
    import onnx
    if args.format == "fp16":
        from onnxconverter_common import float16
        converted = float16.convert_float_to_float16(onnx.load(str(args.model)), keep_io_types=True)
        onnx.save(converted, str(output))
        calibration = "none; float32 input/output preserved"
    elif args.format == "int8-dynamic":
        # Weight-only int8: no calibration pass. Static calibration at 1024 px keeps every
        # intermediate activation in memory and fails with "bad allocation" on the host.
        from onnxruntime.quantization import QuantType, quantize_dynamic
        quantize_dynamic(str(args.model), str(output), per_channel=True,
                         weight_type=QuantType.QInt8)
        calibration = "none (dynamic, weight-only int8); no sample-set access"
    else:
        import numpy as np
        from onnxruntime.quantization import CalibrationDataReader, QuantFormat, QuantType, quantize_static

        class SyntheticReader(CalibrationDataReader):
            def __init__(self):
                self.random = np.random.default_rng(51)
                self.remaining = 8

            def get_next(self):
                if not self.remaining:
                    return None
                self.remaining -= 1
                data = self.random.random((1, settings["height"], settings["width"], 3), dtype=np.float32)
                data = (data - np.array(settings["mean"], dtype=np.float32)) / np.array(settings["std"], dtype=np.float32)
                if settings["input_layout"] == "NCHW":
                    data = data.transpose(0, 3, 1, 2).copy()
                return {settings["input_name"]: data}

        quantize_static(str(args.model), str(output), SyntheticReader(),
                        quant_format=QuantFormat.QDQ, per_channel=True,
                        activation_type=QuantType.QUInt8, weight_type=QuantType.QInt8)
        calibration = "8 synthetic uniform RGB inputs; numpy seed 51; no sample-set access"
    onnx.checker.check_model(str(output))
    settings["candidate"] += "-" + args.format
    evidence(output, args, settings, {
        "source_sha256": file_hash(args.model), "source_config_sha256": file_hash(args.config),
        "calibration": calibration,
    })


if __name__ == "__main__":
    main()
