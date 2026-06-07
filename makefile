BLD_DIR=build
LINUX_DIR=$(BLD_DIR)/Linux
POPCORN_DIR_LINUX=./Popcorn.Linux
POPCORN_PLAYER_DIR_LINUX=./Popcorn.Player.Linux
POPCORN_SERVER_DIR=./Popcorn.Server
MPVSHARP_DIR=./MPVSharp
MPVSHARP_SRC_DIR=$(MPVSHARP_DIR)/src
MPVSHARP_BIN_DIR=$(MPVSHARP_DIR)/bin

DOTNET_VERSION=net10.0
POPCORN_BIN_DIR=$(POPCORN_DIR_LINUX)/bin/$(CONFIG)/$(DOTNET_VERSION)
POPCORN_PLAYER_BIN_DIR=$(POPCORN_PLAYER_DIR_LINUX)/bin/$(CONFIG)/$(DOTNET_VERSION)
INSTALL_DIR=$(HOME)/.local/bin/Popcorn
SYSTEMD_USER_DIR=$(HOME)/.config/systemd/user
SYSTEMD_ENV_DIR=$(HOME)/.config/popcorn
SYSTEMD_SERVICE_NAME=popcorn-server.service
SYSTEMD_ENV_NAME=popcorn-server.env
SYSTEMD_SRC_DIR=$(POPCORN_SERVER_DIR)/systemd
SYSTEMD_SERVICE_FILE=$(SYSTEMD_USER_DIR)/$(SYSTEMD_SERVICE_NAME)
SYSTEMD_ENV_FILE=$(SYSTEMD_ENV_DIR)/$(SYSTEMD_ENV_NAME)

CONFIG=Release

# Clean output folders
clean:
	rm -rf $(BLD_DIR) $(MPVSHARP_BIN_DIR) $(POPCORN_BIN_DIR) $(POPCORN_PLAYER_BIN_DIR)

# Pull and build MPVSharp
update-mpvsharp:
	git submodule update --init --recursive $(MPVSHARP_DIR)
	git submodule update --remote $(MPVSHARP_DIR)

	# Restore and build MPVSharp
	dotnet restore $(MPVSHARP_DIR)/src/Nickvision.MPVSharp/
	dotnet build $(MPVSHARP_DIR)/src/Nickvision.MPVSharp/ --configuration $(CONFIG)

	# Copy the built DLL to the bin directory
	cp $(MPVSHARP_DIR)/src/Nickvision.MPVSharp/bin/$(CONFIG)/net7.0/Nickvision.MPVSharp.* $(MPVSHARP_DIR)

build-popcorn-web:
	cd ./Popcorn.Web && npm run build

build-popcorn-linux: build-popcorn-web
	dotnet restore $(POPCORN_DIR_LINUX)
	dotnet build $(POPCORN_DIR_LINUX) --configuration $(CONFIG)

build-player-web:
	cd ./Popcorn.Player.Web && npm run build

build-popcorn-player-linux: build-player-web
	dotnet restore $(POPCORN_PLAYER_DIR_LINUX)
	dotnet build $(POPCORN_PLAYER_DIR_LINUX) --configuration $(CONFIG)
# Bundle both projects together
bundle-linux:
	rm -rf $(LINUX_DIR)
	mkdir -p $(LINUX_DIR)
	cp -r $(POPCORN_BIN_DIR)/* $(LINUX_DIR)
	cp -r $(POPCORN_PLAYER_BIN_DIR)/* $(LINUX_DIR)

install-linux:
	-systemctl --user stop $(SYSTEMD_SERVICE_NAME)
	rm -rf $(INSTALL_DIR)
	mkdir -p $(INSTALL_DIR)
	cp -r $(LINUX_DIR)/* $(INSTALL_DIR)
	mkdir -p $(SYSTEMD_USER_DIR) $(SYSTEMD_ENV_DIR)
	test -f $(SYSTEMD_ENV_FILE) || cp $(SYSTEMD_SRC_DIR)/$(SYSTEMD_ENV_NAME) $(SYSTEMD_ENV_FILE)
	sed \
		-e 's|@INSTALL_DIR@|$(INSTALL_DIR)|g' \
		-e 's|@ENV_FILE@|$(SYSTEMD_ENV_FILE)|g' \
		$(SYSTEMD_SRC_DIR)/$(SYSTEMD_SERVICE_NAME) > $(SYSTEMD_SERVICE_FILE)
	systemctl --user daemon-reload
	systemctl --user enable --now $(SYSTEMD_SERVICE_NAME)

all: clean build-popcorn-linux build-popcorn-player-linux bundle-linux
