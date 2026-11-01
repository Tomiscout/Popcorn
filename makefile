BLD_DIR=build
LINUX_DIR=$(BLD_DIR)/Linux
POPCORN_DIR_LINUX=./Popcorn.Linux
POPCORN_PLAYER_DIR_LINUX=./Popcorn.Player.Linux
POPCORN_SERVER_DIR=./Popcorn.Server
MPVSHARP_DIR=./MPVSharp
MPVSHARP_SRC_DIR=$(MPVSHARP_DIR)/src
MPVSHARP_BIN_DIR=$(MPVSHARP_DIR)/bin
MPVSHARP_PROJ=$(MPVSHARP_SRC_DIR)/Nickvision.MPVSharp
MPVSHARP_DLL=$(MPVSHARP_DIR)/Nickvision.MPVSharp.dll

DOTNET_VERSION=net10.0
POPCORN_SERVER_BIN_DIR=$(POPCORN_SERVER_DIR)/bin/$(CONFIG)/$(DOTNET_VERSION)
POPCORN_PLAYER_BIN_DIR=$(POPCORN_PLAYER_DIR_LINUX)/bin/$(CONFIG)/$(DOTNET_VERSION)
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
POPCORN_LINUX_EXE=$(POPCORN_DIR_LINUX)/target/release/popcorn-linux
CEF_VERSION=$(shell awk '/^name = "cef-dll-sys"/ { getline; sub(/.*\+/, ""); sub(/"/, ""); print }' $(POPCORN_DIR_LINUX)/Cargo.lock)
CEF_DIR=$(CEF_PATH)/$(CEF_VERSION)/cef_linux_$(shell uname -m)
CEF_RUNTIME_FILES=libcef.so chrome-sandbox chrome_100_percent.pak chrome_200_percent.pak resources.pak \
	icudtl.dat v8_context_snapshot.bin libvk_swiftshader.so libvulkan.so.1 vk_swiftshader_icd.json

.PHONY: clean mpvsharp update-mpvsharp npm-ci npm-ci-popcorn-web npm-ci-player-web build-popcorn-web build-popcorn-server build-popcorn-linux build-player-web build-popcorn-player-linux bundle-linux install-linux all

# Clean output folders
clean:
	rm -rf $(BLD_DIR) $(MPVSHARP_BIN_DIR) $(POPCORN_SERVER_BIN_DIR) $(POPCORN_PLAYER_BIN_DIR)

# Build MPVSharp (Tomiscout fork, git submodule) at the commit pinned in this repo.
# Only runs when the DLL is missing; use update-mpvsharp to rebuild.
$(MPVSHARP_DLL):
	git submodule update --init --recursive $(MPVSHARP_SRC_DIR)
	dotnet restore $(MPVSHARP_PROJ)
	dotnet build $(MPVSHARP_PROJ) --configuration $(CONFIG)
	cp $(MPVSHARP_PROJ)/bin/$(CONFIG)/net7.0/Nickvision.MPVSharp.* $(MPVSHARP_DIR)

mpvsharp: $(MPVSHARP_DLL)

# Pull latest fork main into the submodule and rebuild MPVSharp
update-mpvsharp:
	git submodule update --init --recursive $(MPVSHARP_SRC_DIR)
	git submodule update --remote $(MPVSHARP_SRC_DIR)
	rm -f $(MPVSHARP_DLL)
	$(MAKE) mpvsharp

# Install node dependencies (clean, from package-lock.json)
npm-ci-popcorn-web:
	cd ./Popcorn.Web && npm ci

npm-ci-player-web:
	cd ./Popcorn.Player.Web && npm ci

npm-ci: npm-ci-popcorn-web npm-ci-player-web

build-popcorn-web: npm-ci-popcorn-web
	cd ./Popcorn.Web && npm run build

build-popcorn-server: build-popcorn-web
	dotnet restore $(POPCORN_SERVER_DIR)
	dotnet build $(POPCORN_SERVER_DIR) --configuration $(CONFIG)

# Desktop shell (CEF window showing the server's UI)
build-popcorn-linux:
	cd $(POPCORN_DIR_LINUX) && CEF_PATH=$(CEF_PATH) cargo build --release

build-player-web: npm-ci-player-web
	cd ./Popcorn.Player.Web && npm run build

build-popcorn-player-linux: build-player-web $(MPVSHARP_DLL)
	dotnet restore $(POPCORN_PLAYER_DIR_LINUX)
	dotnet build $(POPCORN_PLAYER_DIR_LINUX) --configuration $(CONFIG)
# Bundle server, desktop shell (+ CEF runtime) and player together
bundle-linux:
	rm -rf $(LINUX_DIR)
	mkdir -p $(LINUX_DIR)/locales
	cp -r $(POPCORN_SERVER_BIN_DIR)/* $(LINUX_DIR)
	cp -r $(POPCORN_PLAYER_BIN_DIR)/* $(LINUX_DIR)
	cp $(POPCORN_LINUX_EXE) $(LINUX_DIR)/Popcorn.Linux
	cd $(CEF_DIR) && cp $(CEF_RUNTIME_FILES) $(CURDIR)/$(LINUX_DIR)/
	cp $(CEF_DIR)/locales/*.pak $(LINUX_DIR)/locales/
	strip --strip-unneeded $(LINUX_DIR)/libcef.so
	@test -x $(LINUX_DIR)/Popcorn.Server || { echo "Popcorn.Server executable missing from bundle"; exit 1; }

install-linux:
	@test -x $(LINUX_DIR)/Popcorn.Server || { echo "$(LINUX_DIR)/Popcorn.Server missing, run 'make all' first"; exit 1; }
	@dotnet --list-runtimes | grep -q '^Microsoft.AspNetCore.App 10\.' || \
		{ echo "ASP.NET Core 10 runtime missing (needed by Popcorn.Server). Arch: sudo pacman -S aspnet-runtime"; exit 1; }
	systemctl --user stop $(SYSTEMD_SERVICE_NAME) 2>/dev/null || true
	rm -rf $(INSTALL_DIR)
	mkdir -p $(INSTALL_DIR)
	cp -r $(LINUX_DIR)/* $(INSTALL_DIR)
	cp $(POPCORN_DIR_LINUX)/popcorn.png $(INSTALL_DIR)/popcorn.png
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

all: clean build-popcorn-server build-popcorn-linux build-popcorn-player-linux bundle-linux
