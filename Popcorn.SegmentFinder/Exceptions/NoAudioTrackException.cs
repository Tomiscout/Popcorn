using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Popcorn.SegmentFinder.Exceptions;
internal class NoAudioTrackException : Exception
{
    public NoAudioTrackException(string message) : base($"No audio track found in file: {message}") { }
}
