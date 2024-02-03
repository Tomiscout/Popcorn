# Squirrel.Windows Packaging Guide

## Introduction

This guide helps packaging new version of Popcorn. It uses `Squirrel.Windows` library for packaging and updates.

## Steps

### Build output binaries

Build release of Popcorn and PopcornPlayer. Release build events already move everything to `lib\net45` folder. This folder structure is required by `Squirrel.Windows`

### Update metadata in .nuspec file

Edit the package metadata to bump version number

### Squirrel.Windows packaging (Automatic)
If you don't have NuGet.exe in path, run in Package Management Console:
```
PM>  Install-Package NuGet.CommandLine
```

Then just build `Popcorn.Installer` release build.

It should output release files in `Popcorn.Installer\Releases` folder.

**Tip**: If you get an error stating that ...'Squirrel' is not recognized... then you may simply need to restart Visual Studio so the Package Manager Console will have loaded all the package tools. More info at [documentation](https://github.com/Squirrel/Squirrel.Windows/blob/develop/docs/getting-started/2-packaging.md).