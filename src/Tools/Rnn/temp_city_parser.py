import json
import os

def parse_cities():
    # Define paths starting from the project root for robustness
    try:
        # Assume the script is run from the project root "Morpheus"
        project_root = os.getcwd()
        if not project_root.endswith("Morpheus"):
            print("Warning: Script might not be running from the 'Morpheus' project root.")
            # Attempt a more generic search up the tree
            found = False
            path = os.getcwd()
            for _ in range(4): # Search up to 4 levels
                if os.path.basename(path) == 'Morpheus':
                    project_root = path
                    found = True
                    break
                path = os.path.dirname(path)
            if not found:
                print("FATAL: Could not determine project root.")
                return
        
        cities_dir = os.path.join(project_root, "src", "Tools", "cities")
    except Exception as e:
        print(f"Error determining paths: {e}")
        return

    input_file = os.path.join(cities_dir, "obce.json")
    output_file = os.path.join(cities_dir, "cities_cz.txt")

    print(f"Reading from: {input_file}")
    if not os.path.exists(input_file):
        print(f"FATAL: Input file does not exist at the specified path.")
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
