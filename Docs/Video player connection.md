# Connection between video player and main app

## Flow
* App is started, pipe server is started on random name.
* Name is saved in file
* Video player starts, pipe connects
* File is deleted
* If client disconnects - wait on new pipe name, create file
* If server disconnects or shut down - player should also be killed.

Concept taken from [here](https://nicolasdeory.medium.com/simple-ipc-using-named-pipes-in-net-core-7cb1b29f6dab).