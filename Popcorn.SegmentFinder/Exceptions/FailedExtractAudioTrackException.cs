using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Popcorn.SegmentFinder.Exceptions;
internal class FailedExtractAudioTrackException : Exception
{
    public FailedExtractAudioTrackException(string message) : base($"Failed to extract track audio from: {message}") { }
}
