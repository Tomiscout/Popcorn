# Same layout as build_windows.ps1 / release_windows.ps1: Bundle/<platform> (the bundle), Bundle/Publish/<project>
# (dotnet publish output), Bundle/Release/<version> (release_linux.sh)
BLD_DIR=Bundle
LINUX_DIR=$(BLD_DIR)/Linux
POPCORN_DIR_LINUX=./Popcorn.Linux
POPCORN_PLAYER_DIR_LINUX=./Popcorn.Player.Linux
POPCORN_PLAYER_NATIVE_DIR=./Popcorn.Player.Native
POPCORN_SERVER_DIR=./Popcorn.Server
# Bundled plugins (Docs/Plugins.md): each Plugins/<name>/ is a .NET project with an optional ui/ npm project.
# Their builds assemble into Popcorn.Server/plugins/<id>/, which the server build copies next to itself.
PLUGIN_DIRS=$(wildcard ./Plugins/*/)
# libmpv with the DMA-BUF render API (Tomiscout/mpv fork, git submodule) for the Linux player. Needs libplacebo newer
# than distros ship, built as a meson subproject inside the (gitignored) subprojects/ of the checkout.
MPV_DIR=./MPV
MPV_SRC_DIR=$(MPV_DIR)/src
MPV_BUILD_DIR=$(MPV_DIR)/build
MPV_SO=$(MPV_DIR)/libmpv.so.2
LIBPLACEBO_URL=https://code.videolan.org/videolan/libplacebo.git
LIBPLACEBO_REF=c4de47cd94af6ce15573debe84306e01bca33996
LIBPLACEBO_DIR=$(MPV_SRC_DIR)/subprojects/libplacebo
# Used when meson is not on PATH
MESON_VENV=$(HOME)/.cache/popcorn/meson-venv

DOTNET_VERSION=net10.0
POPCORN_SERVER_BIN_DIR=$(POPCORN_SERVER_DIR)/bin/$(CONFIG)/$(DOTNET_VERSION)
POPCORN_PLAYER_BIN_DIR=$(POPCORN_PLAYER_DIR_LINUX)/bin/$(CONFIG)/$(DOTNET_VERSION)
# Server and player are published with ReadyToRun (framework-dependent, code precompiled to native): no JIT at start-up,
# which matters most for the file name parser's source-generated regexes. Portable RID, like the apphost in
# Directory.Build.targets.
DOTNET_RID ?= linux-$(if $(filter aarch64 arm64,$(shell uname -m)),arm64,x64)
# Version: the VERSION file (Directory.Build.props stamps it into the assemblies), `make all VERSION=1.2.3` overrides it
# (release_linux.sh). Also written into the bundle's release.json, which Popcorn.Installer compares releases with.
VERSION ?= $(shell cat VERSION)
# Package platform (release.json, Popcorn-<version>-<platform>.zip): linux-x64 / linux-arm64, like the RID
RELEASE_PLATFORM=$(DOTNET_RID)
DOTNET_PUBLISH=dotnet publish --configuration $(CONFIG) --runtime $(DOTNET_RID) --self-contained false -p:PublishReadyToRun=true -p:Version=$(VERSION)
PUBLISH_DIR=$(BLD_DIR)/Publish
POPCORN_SERVER_PUBLISH_DIR=$(PUBLISH_DIR)/Popcorn.Server
POPCORN_PLAYER_PUBLISH_DIR=$(PUBLISH_DIR)/Popcorn.Player.Linux
INSTALL_DIR=$(HOME)/.local/bin/Popcorn
SYSTEMD_USER_DIR=$(HOME)/.config/systemd/user
SYSTEMD_ENV_DIR=$(HOME)/.config/popcorn
SYSTEMD_SERVICE_NAME=popcorn.service
SYSTEMD_ENV_NAME=popcorn.env
SYSTEMD_SRC_DIR=$(POPCORN_SERVER_DIR)/systemd
SYSTEMD_SERVICE_FILE=$(SYSTEMD_USER_DIR)/$(SYSTEMD_SERVICE_NAME)
SYSTEMD_ENV_FILE=$(SYSTEMD_ENV_DIR)/$(SYSTEMD_ENV_NAME)
DESKTOP_DIR=$(HOME)/.local/share/applications
DESKTOP_FILE=$(DESKTOP_DIR)/popcorn.desktop

CONFIG=Release

# Popcorn.Linux (Rust + CEF). The cef crate's build script downloads the CEF binary distribution
# matching its version into $(CEF_PATH)/<cef version>/cef_linux_<arch> on first build.
CEF_PATH ?= $(HOME)/.cache/popcorn/cef
# Rust crates: one Cargo workspace (root Cargo.toml), built per crate (-p) into ./target
CARGO_TARGET=./target/release
POPCORN_LINUX_EXE=$(CARGO_TARGET)/popcorn-linux
# Installer / updater (Rust + egui, Popcorn.Installer/README.md): in the bundle as Popcorn.Installer.Linux, which also
# makes Settings → Updates work (UpdateService looks for it next to the server)
POPCORN_INSTALLER_DIR=./Popcorn.Installer
POPCORN_INSTALLER_EXE=./target/installer/popcorn-installer
# Player native layer (Rust): libpopcorn_player.so (P/Invoked by Popcorn.Player.Linux) + CEF helper executable
# Optional libmpv with the DMA-BUF render API (render_dmabuf.h): the one from `make mpv` when it has been built, or
# any other with MPV_LIB=<path>. Bundled next to libpopcorn_player.so (found through its $$ORIGIN rpath); without it
# the player uses the system libmpv and falls back to the OpenGL video output.
MPV_LIB ?= $(wildcard $(MPV_SO))
POPCORN_PLAYER_NATIVE_FILES=$(CARGO_TARGET)/libpopcorn_player.so $(CARGO_TARGET)/popcorn-player-cef
CEF_VERSION=$(shell awk '/^name = "cef-dll-sys"/ { getline; sub(/.*\+/, ""); sub(/"/, ""); print }' Cargo.lock)
CEF_DIR=$(CEF_PATH)/$(CEF_VERSION)/cef_linux_$(shell uname -m)
CEF_RUNTIME_FILES=libcef.so chrome-sandbox chrome_100_percent.pak chrome_200_percent.pak resources.pak \
	icudtl.dat v8_context_snapshot.bin libvk_swiftshader.so libvulkan.so.1 vk_swiftshader_icd.json

.PHONY: clean kill-popcorn mpv update-mpv npm-ci npm-ci-popcorn-web build-popcorn-web build-plugins build-popcorn-server build-popcorn-linux build-player-web build-popcorn-player-native build-popcorn-player-linux build-popcorn-installer bundle-linux install-linux all

# Clean output folders (only Linux's: Bundle/ also holds the Windows bundle and the releases)
clean:
	rm -rf $(LINUX_DIR) $(POPCORN_SERVER_PUBLISH_DIR) $(POPCORN_PLAYER_PUBLISH_DIR) $(POPCORN_SERVER_BIN_DIR) $(POPCORN_PLAYER_BIN_DIR) $(POPCORN_SERVER_DIR)/plugins

# Build libmpv (Tomiscout fork, git submodule) at the commit pinned in this repo.
# Only runs when the library is missing; use update-mpv to rebuild. Not part of `all`: optional, and a slow build.
$(MPV_SO):
	git submodule update --init $(MPV_SRC_DIR)
	test -d $(LIBPLACEBO_DIR) || git clone $(LIBPLACEBO_URL) $(LIBPLACEBO_DIR)
	git -C $(LIBPLACEBO_DIR) fetch origin $(LIBPLACEBO_REF)
	git -C $(LIBPLACEBO_DIR) checkout --detach $(LIBPLACEBO_REF)
	git -C $(LIBPLACEBO_DIR) submodule update --init --recursive
	command -v meson >/dev/null || test -x $(MESON_VENV)/bin/meson || \
		{ python3 -m venv $(MESON_VENV) && $(MESON_VENV)/bin/pip install meson; }
	test -f $(MPV_BUILD_DIR)/build.ninja || PATH="$$PATH:$(MESON_VENV)/bin" meson setup $(MPV_BUILD_DIR) $(MPV_SRC_DIR) \
		-Dbuildtype=release -Dlibmpv=true -Dcplayer=false -Dvulkan=enabled -Dwayland=disabled -Dgl=enabled \
		-Dlibplacebo:demos=false -Dlibplacebo:tests=false
	PATH="$$PATH:$(MESON_VENV)/bin" meson compile -C $(MPV_BUILD_DIR)
	cp -L $(MPV_BUILD_DIR)/libmpv.so.2 $(MPV_SO)

mpv: $(MPV_SO)

# Pull latest fork master into the submodule and rebuild libmpv
update-mpv:
	git submodule update --init $(MPV_SRC_DIR)
	git submodule update --remote $(MPV_SRC_DIR)
	rm -f $(MPV_SO)
	$(MAKE) mpv

# Install node dependencies (clean, from package-lock.json)
npm-ci-popcorn-web:
	cd ./Popcorn.Web && npm ci

npm-ci: npm-ci-popcorn-web

build-popcorn-web: npm-ci-popcorn-web
	cd ./Popcorn.Web && npm run build

# Plugin UI modules need Popcorn.Web's node_modules only for the linked @popcorn/plugin-sdk, not at runtime
build-plugins: npm-ci-popcorn-web
	@for dir in $(PLUGIN_DIRS); do \
		if [ -f $$dir/ui/package.json ]; then (cd $$dir/ui && npm ci && npm run build) || exit 1; fi; \
		dotnet build $$dir --configuration $(CONFIG) || exit 1; \
	done

build-popcorn-server: build-popcorn-web build-plugins
	rm -rf $(POPCORN_SERVER_PUBLISH_DIR)
	$(DOTNET_PUBLISH) $(POPCORN_SERVER_DIR) --output $(POPCORN_SERVER_PUBLISH_DIR)

# Desktop shell (CEF window showing the server's UI)
build-popcorn-linux:
	CEF_PATH=$(CEF_PATH) cargo build --release -p popcorn-linux

build-popcorn-installer:
	cargo build --profile installer -p popcorn-installer

# Player overlay: Popcorn.Web's player UI as a standalone page (player.html), post-build copies it to Popcorn.Player.Linux/web-player
build-player-web: npm-ci-popcorn-web
	cd ./Popcorn.Web && npm run build:player

# Player window / video / CEF overlay (Rust cdylib, shares the CEF runtime with Popcorn.Linux)
build-popcorn-player-native:
	CEF_PATH=$(CEF_PATH) cargo build --release -p popcorn-player-native

build-popcorn-player-linux: build-player-web build-popcorn-player-native
	rm -rf $(POPCORN_PLAYER_PUBLISH_DIR)
	$(DOTNET_PUBLISH) $(POPCORN_PLAYER_DIR_LINUX) --output $(POPCORN_PLAYER_PUBLISH_DIR)

# Bundle server, desktop shell (+ CEF runtime) and player together
bundle-linux:
	rm -rf $(LINUX_DIR)
	mkdir -p $(LINUX_DIR)/locales
	# Player first, server last: both ship Popcorn.Core/Platform.dll and the server's
	# copies must win (a stale player build would otherwise break the server with MissingMethodException).
	cp -r $(POPCORN_PLAYER_PUBLISH_DIR)/* $(LINUX_DIR)
	cp -r $(POPCORN_SERVER_PUBLISH_DIR)/* $(LINUX_DIR)
	cp $(POPCORN_LINUX_EXE) $(LINUX_DIR)/Popcorn.Linux
	cp $(POPCORN_PLAYER_NATIVE_FILES) $(LINUX_DIR)/
	$(if $(MPV_LIB),cp -L $(MPV_LIB) $(LINUX_DIR)/libmpv.so.2)
	cd $(CEF_DIR) && cp $(CEF_RUNTIME_FILES) $(CURDIR)/$(LINUX_DIR)/
	cp $(CEF_DIR)/locales/*.pak $(LINUX_DIR)/locales/
	strip --strip-unneeded $(LINUX_DIR)/libcef.so
	install -m 755 $(POPCORN_INSTALLER_EXE) $(LINUX_DIR)/Popcorn.Installer.Linux
	cp $(POPCORN_DIR_LINUX)/popcorn.png $(LINUX_DIR)/popcorn.png
	# Templates Popcorn.Installer writes the systemd unit / env file / desktop entry from (the installed version's,
	# not the ones built into an older installer)
	mkdir -p $(LINUX_DIR)/setup
	cp $(SYSTEMD_SRC_DIR)/$(SYSTEMD_SERVICE_NAME) $(SYSTEMD_SRC_DIR)/$(SYSTEMD_ENV_NAME) $(POPCORN_DIR_LINUX)/popcorn.desktop $(LINUX_DIR)/setup/
	# release.json: the installed version for Popcorn.Installer, and the marker of a package zip's root
	printf '{\n  "version": "%s",\n  "platform": "%s",\n  "commit": "%s",\n  "date": "%s"\n}\n' \
		"$(VERSION)" "$(RELEASE_PLATFORM)" "$$(git rev-parse --short HEAD 2>/dev/null)" "$$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
		> $(LINUX_DIR)/release.json
	@test -x $(LINUX_DIR)/Popcorn.Server || { echo "Popcorn.Server executable missing from bundle"; exit 1; }

# Stop running installed instances: desktop shell (+ CEF subprocesses, same exe), native player and a server the
# shell spawned outside systemd. Replacing the files under them otherwise leaves the app frozen.
# The pattern is anchored to argv[0] so it never matches this recipe's own shell.
POPCORN_PROCS='^$(INSTALL_DIR)/Popcorn\.(Linux|Player\.Linux|Server)( |$$)'
kill-popcorn:
	@if pgrep -f $(POPCORN_PROCS) >/dev/null; then \
		echo "Stopping running Popcorn instances"; \
		pkill -TERM -f $(POPCORN_PROCS); \
		for i in 1 2 3 4 5 6; do pgrep -f $(POPCORN_PROCS) >/dev/null || exit 0; sleep 0.5; done; \
		echo "Still running after 3s, killing"; \
		pkill -KILL -f $(POPCORN_PROCS) || true; \
	fi

install-linux:
	@test -x $(LINUX_DIR)/Popcorn.Server || { echo "$(LINUX_DIR)/Popcorn.Server missing, run 'make all' first"; exit 1; }
	@dotnet --list-runtimes | grep -q '^Microsoft.AspNetCore.App 10\.' || \
		{ echo "ASP.NET Core 10 runtime missing (needed by Popcorn.Server). Arch: sudo pacman -S aspnet-runtime"; exit 1; }
	systemctl --user stop $(SYSTEMD_SERVICE_NAME) 2>/dev/null || true
	$(MAKE) --no-print-directory kill-popcorn
	rm -rf $(INSTALL_DIR)
	mkdir -p $(INSTALL_DIR)
	cp -r $(LINUX_DIR)/* $(INSTALL_DIR)
	mkdir -p $(DESKTOP_DIR)
	test -f $(DESKTOP_FILE) || sed -e 's|@INSTALL_DIR@|$(INSTALL_DIR)|g' $(POPCORN_DIR_LINUX)/popcorn.desktop > $(DESKTOP_FILE)
	mkdir -p $(SYSTEMD_USER_DIR) $(SYSTEMD_ENV_DIR)
	test -f $(SYSTEMD_ENV_FILE) || cp $(SYSTEMD_SRC_DIR)/$(SYSTEMD_ENV_NAME) $(SYSTEMD_ENV_FILE)
	sed \
		-e 's|@INSTALL_DIR@|$(INSTALL_DIR)|g' \
		-e 's|@ENV_FILE@|$(SYSTEMD_ENV_FILE)|g' \
		$(SYSTEMD_SRC_DIR)/$(SYSTEMD_SERVICE_NAME) > $(SYSTEMD_SERVICE_FILE)
	systemctl --user daemon-reload
	systemctl --user enable --now $(SYSTEMD_SERVICE_NAME)

all: clean build-popcorn-server build-popcorn-linux build-popcorn-player-linux build-popcorn-installer bundle-linux
