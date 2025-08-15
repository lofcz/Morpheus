import csv
import os
import sys
import hashlib
import tempfile


def main() -> int:
	# Source CSV and output files live in this directory
	base_dir = os.path.dirname(os.path.abspath(__file__))
	csv_path = os.path.join(base_dir, "res_data.csv")
	companies_out_path = os.path.join(base_dir, "companies.txt")
	names_out_path = os.path.join(base_dir, "names.txt")

	# Codes marking physical persons (FO)
	# We will consider FO if EITHER ROSFORMA OR FORMA matches a known FO code.
	# User-provided set extended with ROSFORMA=100 (generic FO in registr osob).
	fo_codes = {"100", "101", "105", "107", "424", "425"}

	row_count = 0
	companies_unique = 0
	names_unique = 0

	# Use hashing buckets to deduplicate without loading everything into memory
	num_buckets = 128  # total bucket files per output
	with tempfile.TemporaryDirectory(dir=base_dir, prefix="split_tmp_") as tmpdir:
		name_bucket_paths = [os.path.join(tmpdir, f"names_{i:03d}.tmp") for i in range(num_buckets)]
		company_bucket_paths = [os.path.join(tmpdir, f"companies_{i:03d}.tmp") for i in range(num_buckets)]

		# Open all bucket files once to avoid frequent open/close
		name_bucket_files = [open(p, "w", encoding="utf-8", newline="\n") for p in name_bucket_paths]
		company_bucket_files = [open(p, "w", encoding="utf-8", newline="\n") for p in company_bucket_paths]

	# Stream read the large CSV and write outputs line-by-line
	# Use utf-8 with robust error handling; CSV appears to be comma-delimited with quoted fields
		with open(csv_path, "r", encoding="utf-8", errors="replace", newline="") as f_in:
			reader = csv.DictReader(f_in)

			# Validate required columns exist
			required_columns = {"ROSFORMA", "FIRMA"}
			missing = [c for c in required_columns if c not in reader.fieldnames]
			if missing:
				print(f"ERROR: Missing required columns in CSV: {missing}", file=sys.stderr)
				return 2

			for row in reader:
				row_count += 1

				rosforma = (row.get("ROSFORMA") or "").strip()
				forma = (row.get("FORMA") or "").strip()
				firma = (row.get("FIRMA") or "").strip()

				if not firma:
					continue

				# Classify: FO if either ROSFORMA or FORMA matches a known FO code
				is_fo = (rosforma in fo_codes) or (forma in fo_codes)

				# Hash-bucketize for later per-bucket dedup
				# Use stable hash (blake2b) on the normalized line content
				line = firma
				digest = hashlib.blake2b(line.encode("utf-8"), digest_size=2).hexdigest()
				bucket_idx = int(digest, 16) % num_buckets
				if is_fo:
					name_bucket_files[bucket_idx].write(line + "\n")
				else:
					company_bucket_files[bucket_idx].write(line + "\n")

		# Close all bucket files before dedup pass
		for fh in name_bucket_files:
			fh.close()
		for fh in company_bucket_files:
			fh.close()

		# Deduplicate per bucket and write final outputs
		with open(names_out_path, "w", encoding="utf-8", newline="\n") as f_names, \
			 open(companies_out_path, "w", encoding="utf-8", newline="\n") as f_companies:
			# Names (FO)
			for p in name_bucket_paths:
				seen = set()
				with open(p, "r", encoding="utf-8", errors="replace") as fb:
					for line in fb:
						val = line.rstrip("\n")
						if not val:
							continue
						if val in seen:
							continue
						seen.add(val)
						f_names.write(val + "\n")
						names_unique += 1

			# Companies (PO)
			for p in company_bucket_paths:
				seen = set()
				with open(p, "r", encoding="utf-8", errors="replace") as fb:
					for line in fb:
						val = line.rstrip("\n")
						if not val:
							continue
						if val in seen:
							continue
						seen.add(val)
						f_companies.write(val + "\n")
						companies_unique += 1

	print(f"Done. Rows read: {row_count}. Unique -> companies: {companies_unique}, names: {names_unique}", file=sys.stderr)
	print(f"companies.txt -> {os.path.relpath(companies_out_path)}")
	print(f"names.txt -> {os.path.relpath(names_out_path)}")
	return 0


if __name__ == "__main__":
	sys.exit(main())


