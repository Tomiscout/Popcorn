const fs = require('fs');
const path = require('path');
const getVersionInfo = require('win-version-info');

// Function to read file names, sizes, and optionally fetch Product Version if a DLL
function readDirectoryWithVersion(dirPath) {
    const files = fs.readdirSync(dirPath);
    const fileData = {};

    files.forEach((file) => {
        const filePath = path.join(dirPath, file);
        const stats = fs.statSync(filePath);

        if (stats.isFile()) {
            fileData[file] = {
                size: stats.size,
                productVersion: null,
            };

            // Check if the file is a DLL and fetch Product Version
            if (file.toLowerCase().endsWith('.dll')) {
                try {
                    const versionInfo = getVersionInfo(filePath);
                    fileData[file].productVersion = versionInfo.ProductVersion || 'Unknown';
                } catch (err) {
                    console.warn(`Failed to read version info for '${file}':`, err.message);
                }
            }
        }
    });

    return fileData;
}

// Directories to compare
const dir1 = '.\\Popcorn\\bin\\Release\\net8.0-windows10.0.19041.0\\win10-x64';
const dir2 = '.\\Popcorn.Player.Windows\\bin\\Release\\net8.0-windows10.0.19041.0';

// Read both directories
const dir1Files = readDirectoryWithVersion(dir1);
const dir2Files = readDirectoryWithVersion(dir2);

// Compare files
Object.keys(dir1Files).forEach((file) => {
    if (dir2Files[file] !== undefined) {
        if (dir1Files[file].size !== dir2Files[file].size) {
            console.log(`File size mismatch for '${file}':`);
            console.log(` - ${dir1}: ${dir1Files[file].size} bytes, ${dir1Files[file].productVersion}`);
            console.log(` - ${dir2}: ${dir2Files[file].size} bytes, ${dir2Files[file].productVersion}`);
        }
    }
});
