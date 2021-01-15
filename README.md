# CryVideoComparer
A simple cross-platform command-line tool for comparing diffent ffmpeg video encoders based on a reference video and VMAF metric. 

User can define multiple presets (= ffmpeg commands) for encoding the reference video. The tool will then calculate the VMAF metric for each defined preset. 


## Requirements
- **FFmpeg** executable available in PATH or in the same directory as this tool


## Usage
```
Usage: ./cryVideoComparer [options] <REFERENCE CLIP> [options]

Options:
   --from <timestamp>      Select the FROM timestamp for reference clip. (example: 52.3)
   --to <timestamp>        Select the TO timestamp for reference clip. (example: 01:10)
   --threads <threads>     Number of threads for calculating VMAF. (default: 8)
   --presets <filename>    Select file that contains all presets you want to compare, separated by lines
```

### Examples
Used reference video is `myClip.mp4`.

Using timestamps `00:45.5` and `01:00` and presets file `presets.txt`:
```
./cryVideoComparer --from 00:45.5 --to 01:00 -p presets.txt myClip.mp4
```

Example using shorthand options. Timestamps `00:05` and `00:15`. Using 16 threads for VMAF calculation:
```
./cryVideoComparer -p presets.txt -f 5 -t 15 myClip.mp4 --threads 16
```

## Presets File
A presets file contains different ffmpeg commands that will encode the reference video. Each preset is in a new line.

Example:
```
-c:v libx265 -preset slow -b:v 2M -f mp4
-c:v libx265 -preset medium -b:v 2M -f mp4
-c:v libx265 -preset fast -b:v 2M -f mp4
-c:v hevc_nvenc -preset slow -b:v 2M -f mp4
```
A preset must define the following:
- video codec (options `-c:v`/`-codec:v`/`-vcodec`)
- output format (option `-f`)

A preset must not have any inputs (`-i`)

Anything else is up to you. You can set encoder parameters however you want.


---


## Publishing
You can publish this tool from source code by running the following command:
```
dotnet publish -c Release -r win-x64 -o output
``` 
It will generate the executable for Windows insdie the `output` directory.

If you want to publish for other platforms, check the availabe runtime identifiers for dotnet.
(Linux: `linux-x64`)