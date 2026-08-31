import argparse
import csv
from pathlib import Path

try:
    from better_profanity import profanity
except ImportError as error:
    raise SystemExit(
        "Error: better-profanity is not installed. "
        "Install it with 'python -m pip install better-profanity'."
    ) from error


def parse_args():
    parser = argparse.ArgumentParser(
        description="Remove CSV rows whose Word value contains profanity."
    )
    parser.add_argument("csv_file", type=Path, help="CSV file to sanitize")
    return parser.parse_args()


def sanitized_path(input_path):
    return input_path.with_name(f"{input_path.stem}-sanitized.csv")


def sanitize(input_path):
    output_path = sanitized_path(input_path)
    profanity.load_censor_words()

    with input_path.open("r", encoding="utf-8-sig", newline="") as input_file:
        reader = csv.DictReader(input_file)
        if reader.fieldnames is None:
            raise ValueError("Input CSV is missing a header row.")
        if "Word" not in reader.fieldnames:
            raise ValueError("Input CSV must contain a 'Word' column.")

        with output_path.open("w", encoding="utf-8", newline="") as output_file:
            writer = csv.DictWriter(output_file, fieldnames=reader.fieldnames)
            writer.writeheader()

            removed_count = 0
            retained_count = 0
            for row in reader:
                if profanity.contains_profanity(row["Word"] or ""):
                    removed_count += 1
                    continue

                writer.writerow(row)
                retained_count += 1

    return output_path, retained_count, removed_count


def main():
    args = parse_args()

    try:
        output_path, retained_count, removed_count = sanitize(args.csv_file)
    except (OSError, csv.Error, ValueError) as error:
        raise SystemExit(f"Error: {error}") from error

    print(
        f"Wrote {retained_count} rows to {output_path} "
        f"({removed_count} removed)."
    )


if __name__ == "__main__":
    main()
