
using LibVLCSharp.Shared;
using Serilog;
using System.Text.Json;
using System.Threading;
using System;
using PopcornPlayer.Domain.Extensions;
using Popcorn.Player.Extensions;

namespace Popcorn.Player.Core;
public static class MediaPlayback
{

    public static bool PlayNextTitle()
    {
        var nextItem = MediaItemManager.CurrentMediaItem?.GetNextMediaFile();
        if (nextItem is null) return false;

        PlayerCore.LoadMedia(nextItem);

        return true;
    }

    public static bool PlayPause(object data)
    {
        try
        {
            var play = ((JsonElement)data).GetBoolean();
            if (play)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    if (PlayerCore.MediaPlayer.State == VLCState.Ended) // When track ends it needs to be reloaded
                    {
                        var currentTime = PlayerCore.MediaPlayer.Time;
                        PlayerCore.MediaPlayer.Play(PlayerCore.CurrentMediaInstance);
                        PlayerCore.MediaPlayer.Time = currentTime;
                    }
                    else
                    {
                        PlayerCore.MediaPlayer.Play();
                    }
                });
            }
            else
            {
                ThreadPool.QueueUserWorkItem(_ => PlayerCore.MediaPlayer.Pause());
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed media player play/pause");
            return false;
        }
        return true;
    }

    public static bool SetPlaybackTimeSkip(object data)
    {
        try
        {
            var time = ((JsonElement)data).GetInt64();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                PlayerCore.MediaPlayer.Time += time * 1000;
                PlayerCore.PostEvent("current_time", PlayerCore.MediaPlayer.Time);
            });
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to playback skip");
            return false;
        }
        return true;
    }

    public static bool SetPlaybackTime(object data)
    {
        try
        {
            var time = data.GetJsonInt64();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (PlayerCore.MediaPlayer.State == VLCState.Ended) // When track ends it needs to be reloaded
                {
                    PlayerCore.MediaPlayer.Play(PlayerCore.CurrentMediaInstance);
                }
                PlayerCore.MediaPlayer.Time = time;

            });
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to set playback time");
            return false;
        }
        return true;
    }
}
