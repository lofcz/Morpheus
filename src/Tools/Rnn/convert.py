import torch
import os
import argparse
from train import NerClassifier, CHAR_MAX_LEN

def main():
    SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
    parser = argparse.ArgumentParser(description="Convert a PyTorch checkpoint to ONNX.")
    parser.add_argument(
        "--checkpoint",
        default=os.path.join(SCRIPT_DIR, "checkpoints", "best.pt"),
        help="Path to the input PyTorch checkpoint (.pt file)."
    )
    parser.add_argument(
        "--output",
        default=os.path.join(SCRIPT_DIR, "name_classifier.onnx"),
        help="Path to the output ONNX model file."
    )
    args = parser.parse_args()

    if not os.path.exists(args.checkpoint):
        print(f"Error: Checkpoint file not found at {args.checkpoint}")
        return

    print(f"Loading checkpoint from {args.checkpoint}...")
    ckpt = torch.load(args.checkpoint, map_location="cpu")

    # Recreate model from config stored in checkpoint
    config = ckpt['config']
    model = NerClassifier(
        vocab_size=config['VOCAB_SIZE'],
        embedding_dim=config['EMBEDDING_DIM'],
        hidden_dim=config['HIDDEN_DIM'],
        output_dim=config['OUTPUT_DIM'],
        n_layers=config.get("NUM_LAYERS", 2),  # Use .get for backward compatibility
        dropout=config.get("DROPOUT", 0.3)      # Use .get for backward compatibility
    )

    model.load_state_dict(ckpt['model_state_dict'])
    model.eval()

    print(f"Exporting model to {args.output}...")

    dummy_input_ids = torch.randint(0, config['VOCAB_SIZE'], (1, config['MAX_LEN']), dtype=torch.long)
    dummy_byte_ids = torch.zeros((1, config['MAX_LEN'], CHAR_MAX_LEN), dtype=torch.long)
    input_names = ["input_ids", "byte_ids"]
    output_names = ["logits"]

    torch.onnx.export(
        model,
        (dummy_input_ids, dummy_byte_ids),
        args.output,
        input_names=input_names,
        output_names=output_names,
        opset_version=14,
        dynamic_axes={
            'input_ids': {0: 'batch_size', 1: 'sequence_length'},
            'byte_ids': {0: 'batch_size', 1: 'sequence_length', 2: 'char_length'},
            'logits': {0: 'batch_size', 1: 'sequence_length'}
        },
        # Use the modern torch.export-based exporter to align with future PyTorch versions.
        # This silences the DeprecationWarning.
        # dynamo=True
    )

    print("--- Conversion Finished ---")
    print(f"Model successfully exported to {args.output}")

if __name__ == "__main__":
    main()
