using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Popcorn.SegmentFinder.Exceptions;
internal class NoAudioDurationException : Exception
{
    public NoAudioDurationException(string message) : base($"No audio duration found in file: {message}") { }
}
