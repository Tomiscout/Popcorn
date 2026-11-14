#!/usr/bin/env node
// Sets the product version everywhere it is declared, so every build (cargo xtask, make) carries it:
//   VERSION                              .NET assemblies (Directory.Build.props reads it), `cargo xtask`'s default
//                                        (release.json, tag v<version>)
//   Cargo.toml + Cargo.lock              Rust crates (one workspace version; Popcorn.Installer's --version / User-Agent /
//                                        exe file version)
//   Popcorn.Web/package.json + lock
// Not touched: plugin versions (plugin.json, Plugins/*/ui) and Popcorn.Web/plugin-sdk (versioned with PluginApi).
//
//   node set_version.mjs 1.2.0
//   node set_version.mjs v1.3.0-beta.1      # a leading v (tag form) is dropped
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(fileURLToPath(import.meta.url));
const crates = ['Popcorn.Cli', 'Popcorn.Installer', 'Popcorn.Linux', 'Popcorn.Player.Native', 'Popcorn.Player.Windows.Native'];
const npmProjects = ['Popcorn.Web'];

const arg = process.argv[2];
if (!arg || process.argv.length > 3) {
    console.error('Usage: node set_version.mjs <major.minor.patch[-prerelease]>');
    process.exit(2);
}
const version = arg.replace(/^v/, '');
// Same rule as xtask/src/util.rs (is_version).
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

    // The crates take `version.workspace = true`
    update('Cargo.toml', (text) => replaceOnce(text, /(\[workspace\.package\][^[]*?^version\s*=\s*")[^"]*(")/m));
    // The crates' own entries; otherwise the next cargo build rewrites them (and --locked fails).
    const names = crates.map((crate) =>
        /\[package\][^[]*?^name\s*=\s*"([^"]+)"/m.exec(readFileSync(join(root, crate, 'Cargo.toml'), 'utf8'))[1]);
    update('Cargo.lock', (text) => {
        for (const name of names) {
            text = replaceOnce(text, new RegExp(`(\\[\\[package\\]\\]\\r?\\nname = "${name}"\\r?\\nversion = ")[^"]*(")`));
            if (text === null) {
                return null;
            }
        }
        return text;
    });

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
