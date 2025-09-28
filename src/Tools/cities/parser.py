import json
import os

def parse_cities():
    # The script is in the same directory as the data file
    script_dir = os.path.dirname(os.path.realpath(__file__))
    
    input_file = os.path.join(script_dir, "obce.json")
    output_file = os.path.join(script_dir, "cities_cz.txt")

    print(f"Reading from: {input_file}")
    if not os.path.exists(input_file):
        print(f"FATAL: Input file 'obce.json' not found in the same directory as the script.")
        return

    print(f"Writing to: {output_file}")

    try:
        with open(input_file, 'r', encoding='utf-8') as infile, \
             open(output_file, 'w', encoding='utf-8') as outfile:
            
            data = json.load(infile)
            municipalities = data.get('municipalities', [])
            
            if not municipalities:
                print("Warning: No 'municipalities' key found in JSON or the list is empty.")
                return

            count = 0
            for item in municipalities:
                city_name = item.get('hezkyNazev')
                if city_name:
                    outfile.write(city_name.strip() + '\n')
                    count += 1
            
            print(f"Successfully processed and wrote {count:,} city names.")

    except json.JSONDecodeError:
        print(f"FATAL: Could not decode JSON from {input_file}. The file might be corrupted.")
    except Exception as e:
        print(f"An unexpected error occurred: {e}")

if __name__ == "__main__":
    parse_cities()
