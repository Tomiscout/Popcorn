#!/usr/bin/env node
// Sets the product version everywhere it is declared, so every build (make, build_windows.ps1) carries it:
//   VERSION                              .NET assemblies (Directory.Build.props reads it), build_windows.ps1 /
//                                        release_windows.ps1 default (release.json, tag v<version>)
//   */Cargo.toml + Cargo.lock            Rust crates (Popcorn.Installer's --version / User-Agent / exe file version)
//   Popcorn.Web/package.json + lock
// Not touched: plugin versions (plugin.json, Plugins/*/ui) and Popcorn.Web/plugin-sdk (versioned with PluginApi).
//
//   node set_version.mjs 1.2.0
//   node set_version.mjs v1.3.0-beta.1      # a leading v (tag form) is dropped
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(fileURLToPath(import.meta.url));
const crates = ['Popcorn.Installer', 'Popcorn.Linux', 'Popcorn.Player.Native', 'Popcorn.Player.Windows.Native'];
const npmProjects = ['Popcorn.Web'];

const arg = process.argv[2];
if (!arg || process.argv.length > 3) {
    console.error('Usage: node set_version.mjs <major.minor.patch[-prerelease]>');
    process.exit(2);
}
const version = arg.replace(/^v/, '');
// Same rule as build_windows.ps1 / release_windows.ps1.
if (!/^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$/.test(version)) {
    console.error(`Version '${arg}' is not major.minor.patch[-prerelease]`);
    process.exit(2);
}

// Edits keep each file's own line endings.
function update(relPath, change) {
    const path = join(root, relPath);
    const text = readFileSync(path, 'utf8');
    const eol = text.includes('\r\n') ? '\r\n' : '\n';
    const result = change(text, eol);
    if (result === null) {
        throw new Error(`${relPath}: version not found`);
    }
    if (result !== text) {
        writeFileSync(path, result);
    }
    console.log(`${result === text ? 'unchanged' : 'updated  '} ${relPath}`);
}

function replaceOnce(text, pattern) {
    if (!pattern.test(text)) {
        return null;
    }
    return text.replace(pattern, (_, before, after) => `${before}${version}${after}`);
}

function updateJson(text, eol, apply) {
    const json = JSON.parse(text);
    apply(json);
    return JSON.stringify(json, null, 2).replace(/\n/g, eol) + eol;
}

try {
    update('VERSION', (_, eol) => version + eol);

    for (const crate of crates) {
        let name;
        update(`${crate}/Cargo.toml`, (text) => {
            name = /\[package\][^[]*?^name\s*=\s*"([^"]+)"/m.exec(text)?.[1];
            return replaceOnce(text, /(\[package\][^[]*?^version\s*=\s*")[^"]*(")/m);
        });
        // The crate's own entry; otherwise the next cargo build rewrites it (and --locked fails).
        update(`${crate}/Cargo.lock`, (text) =>
            replaceOnce(text, new RegExp(`(\\[\\[package\\]\\]\\r?\\nname = "${name}"\\r?\\nversion = ")[^"]*(")`)));
    }

    for (const project of npmProjects) {
        update(`${project}/package.json`, (text, eol) => updateJson(text, eol, (json) => {
            json.version = version;
        }));
        update(`${project}/package-lock.json`, (text, eol) => updateJson(text, eol, (json) => {
            json.version = version;
            json.packages[''].version = version;
        }));
    }
} catch (e) {
    console.error(e.message);
    process.exit(1);
}
console.log(`Version ${version}`);
