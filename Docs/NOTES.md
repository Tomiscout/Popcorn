
# TODO - linux

Item history not working
Socket
Prevent from sleep
Auto select subtitles regression

Popcorn settings not saving
Investigate gpu-next

Subtitle box adjusting

# Development environment #

Run `npm install` in `Popcorn.Web/` (main UI and the native players' overlay, `npm run build:player`).
See `CLAUDE.md` for project structure, build and run instructions.



### Script for converting windows db to linux db
```sql
UPDATE MediaFile 
SET file_path = 
    CASE 
        WHEN file_path LIKE 'D:\%' THEN '/media/data' || SUBSTR(REPLACE(REPLACE(file_path, '\', '/'), '\\', '/'), 3)
        WHEN file_path LIKE 'C:\%' THEN '/media/windows' || SUBSTR(REPLACE(REPLACE(file_path, '\', '/'), '\\', '/'), 3)
        ELSE file_path 
    END;
   
UPDATE MediaItem 
SET folder_directory = 
    CASE 
        WHEN folder_directory LIKE 'D:\%' THEN '/media/data' || SUBSTR(REPLACE(REPLACE(folder_directory, '\', '/'), '\\', '/'), 3)
        WHEN folder_directory LIKE 'C:\%' THEN '/media/windows' || SUBSTR(REPLACE(REPLACE(folder_directory, '\', '/'), '\\', '/'), 3)
        ELSE folder_directory 
    END;   
    

```
