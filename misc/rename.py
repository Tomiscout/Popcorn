import os

# Define the directory path you want to search in
directory_path = 'C:/Projects/Popcorn'

# Define the search and replacement terms
search_terms = ['VideoBlazor', 'VideoPlayer']
replacement_terms = ['Popcorn', 'PopcornPlayer']

# Function to rename files
def rename_files(path):
    for root, _, files in os.walk(path):
        for name in files:
            for search_term, replacement_term in zip(search_terms, replacement_terms):
                if search_term in name:
                    new_name = name.replace(search_term, replacement_term)
                    os.rename(os.path.join(root, name), os.path.join(root, new_name))

# Function to rename folders
def rename_folders(path):
    for root, dirs, _ in os.walk(path, topdown=False):
        for name in dirs:
            for search_term, replacement_term in zip(search_terms, replacement_terms):
                if search_term in name:
                    new_name = name.replace(search_term, replacement_term)
                    os.rename(os.path.join(root, name), os.path.join(root, new_name))

# Call the functions to rename files and folders
rename_files(directory_path)
rename_folders(directory_path)
