import subprocess
import time

def run_command(command):
    start = time.time()
    subprocess.run(command, shell=True)
    end = time.time()
    return end - start

def average_time(command, runs=2):
    total_time = sum(run_command(command) for _ in range(runs))
    return total_time / runs

root = r'C:\\Users\\brazi\\Downloads\Video samples\\'
hlg = fr'{root}iPhone11_4K-recorder_59.940DV+HLG.mov'
hevc_simple = fr'{root}h265\AVATAR-TWOW_TLR-2_1920x804_HEVC_420_10bit_DTS-HD_MA_AC3_51-thedigitaltheater.mkv'
h264 = fr'{root}h264\h264 mov.mov'

input = f'"{hlg}"'
outputDir = r"C:\\Users\\brazi\\AppData\\Roaming\\Popcorn\\thumb\\"

size= "160x90"

cuvid_h264 = fr'ffmpeg -hwaccel cuvid -hwaccel_output_format cuda -c:v h264_cuvid -ss 00:00:10.000 -resize {size} -an -i {input} -vf "thumbnail_cuda=2,hwdownload,format=nv12" -vframes 1 "{outputDir}cuvid.jpg" -y'
cuvid_hevc = fr'ffmpeg -hwaccel cuvid -hwaccel_output_format cuda -c:v hevc_cuvid -ss 00:00:10.000 -resize {size} -an -i {input} -vf "thumbnail_cuda=2,hwdownload,format=p010le,zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=mobius:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p" -vframes 1 "{outputDir}cuvid.jpg" -y'
dx9 = fr'ffmpeg -hwaccel dxva2 -ss 00:00:10.000 -resize {size} -an -i {input} -vf "zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=mobius:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p" -vframes 1 "{outputDir}dx11.jpg" -y'
dx11 = fr'ffmpeg -hwaccel d3d11va -ss 00:00:10.000 -resize {size} -an -i {input} -vf "zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=mobius:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p" -vframes 1 "{outputDir}dx11.jpg" -y'
cpu = fr'ffmpeg -ss 00:00:10.000 -resize {size} -i {input} -vframes 1 -q:v 2 "{outputDir}cpu.jpg" -y'

avg_cuvid = average_time(cuvid_hevc)
avg_dx9 = average_time(dx9)
avg_dx11 = average_time(dx11)
#avg_cpu = average_time(cpu)

print(f"Average time for cuvid: {avg_cuvid:.3f} seconds")
print(f"Average time for dx9 (apu): {avg_dx11:.3f} seconds")
print(f"Average time for dx11 (apu): {avg_dx11:.3f} seconds")
#print(f"Average time for cpu: {avg_cpu:.3f} seconds")