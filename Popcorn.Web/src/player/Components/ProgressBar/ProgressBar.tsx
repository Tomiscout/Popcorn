import { useEffect, useState, useRef } from 'react';
import { postMessage, registerEventHandler } from '../../../utils/events';
import './ProgressBar.css';
import debounce from 'lodash.debounce';
import SkipButton from '../SkipButton/SkipButton';
import { usePlayerStore } from '../../../state/player.store';
import { useWebSocket } from '../../../state/websocket-context';
import { SocketRequests } from '../../../socket/socket-requests';
import { Slider } from '@mantine/core';

const ProgressBar = () => {
  const { fileId, currentTime, duration, thumbnailFiles, setCurrentTime } = usePlayerStore();
  const { client } = useWebSocket();

  const [popupVisible, setPopupVisible] = useState(false);
  const [popupTime, setPopupTime] = useState('');
  const [popupX, setPopupX] = useState(0);
  const [lineX, setLineX] = useState(0);
  const progressBarRef = useRef<HTMLDivElement>(null);

  const [thumbnail, setThumbnail] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  //const [currentTime, setCurrentTime] = useState(0);
  // const [totalTime, setTotalTime] = useState(0);
  // const totalTimeRef = useRef(totalTime); //Needed for segments calculation and immediate access to value. could be avoided with precalculated end from backend
  const [timeSegments, setTimeSegments] = useState([]);
  const timeSegmentsRef = useRef(timeSegments); //withou using ref - handleCurrentTime closure cant access newest segments, just default []
  const [introButtonVisible, setIntroButtonVisible] = useState(false);
  const [outroButtonVisible, setOutroButtonVisible] = useState(false);
  const introButtonVisibleRef = useRef(introButtonVisible);
  const outroButtonVisibleRef = useRef(outroButtonVisible);

  // const [skipRequested, setSkipRequested] = useState(false);
  const [currentSkipTimeEnd, setCurrentSkipTimeEnd] = useState(0);
  const currentSkipTimeEndRef = useRef(currentSkipTimeEnd);

  const playbackReset = () => {
    setThumbnail(null);
    // clearThumbCache();
    //setCurrentTime(0);
    // setTotalTime(0);
    setTimeSegments([]);
    setIntroButtonVisible(false);
    setOutroButtonVisible(false);
    // setSkipRequested(false);
    setCurrentSkipTimeEnd(0);
  };

  useEffect(() => {
    //registerEventHandler('current_time', (data) => handleCurrentTime(data * 1000)); //TODO vlc uses ms, while mpv returns seconds double. Should convert logic to mpv
    // registerEventHandler('segments_update', (data) => setSegments(data));
    // registerEventHandler('duration_update', (data) => {
    //   if (data === -1) {
    //     setTotalTime(0);
    //     //TODO handle this better
    //   } else {
    //     setTotalTime(data * 1000);
    //   }
    // });

    // registerEventHandler('track_loaded', () => clearThumbCache());
    registerEventHandler('playback_reset', playbackReset);
    //registerSharedBufferHandler('thumb', thumbnailCallback);
  });

  useEffect(() => {
    timeSegmentsRef.current = timeSegments;
  }, [timeSegments]);
  useEffect(() => {
    currentSkipTimeEndRef.current = currentSkipTimeEnd;
  }, [currentSkipTimeEnd]);
  useEffect(() => {
    introButtonVisibleRef.current = introButtonVisible;
    outroButtonVisibleRef.current = outroButtonVisible;
  }, [introButtonVisible, outroButtonVisible]);

  useEffect(() => {
    if (!lastRequestedTime.current) return;
    const blobUrl = thumbnailFiles[getThumbnailKey(lastRequestedTime.current)] || null;
    if (blobUrl) {
      setThumbnail(blobUrl);
      setIsLoading(false);
    }
  }, [thumbnailFiles]);

  function getThumbnailKey(timeMs: number) {
    return `thumbnail_${fileId}_${timeMs / 1000}`;
  }
  const handleSliderChange = (newValue: number) => {
    setCurrentTime(newValue);
    postMessage('set_playback_time', newValue); //TODO throttle
  };

  // useEffect(() => {
  //   if (introButtonVisibleRef.current || outroButtonVisibleRef.current) {
  //     // Finds current segment and check if still in range
  //     for (const segment of timeSegmentsRef.current) {
  //       if (segment.end !== currentSkipTimeEndRef.current) continue;

  //       if (currentTime < segment.start || currentTime > segment.end) {
  //         setOutroButtonVisible(false);
  //         setIntroButtonVisible(false);
  //       }
  //       return;
  //     }
  //     return;
  //   }

  //   for (const segment of timeSegmentsRef.current) {
  //     if (segment.start < 0) {
  //       if (segment.start + totalTimeRef.current <= currentTime && segment.end > currentTime) {
  //         setOutroButtonVisible(true);
  //         setCurrentSkipTimeEnd(segment.end);
  //         return;
  //       }
  //     } else if (segment.start <= currentTime && segment.end > currentTime) {
  //       setIntroButtonVisible(true);
  //       setCurrentSkipTimeEnd(segment.end);
  //       return;
  //     }
  //   }
  // }, [currentTime]);

  // const setSegments = (data) => {
  //   const newSegments = [];
  //   if (data.intro) {
  //     newSegments.push(...data.intro);
  //   }
  //   if (data.outro) {
  //     newSegments.push(...data.outro);
  //   }

  //   //Convert to ms, ugly but good for now
  //   const segmentsResult = newSegments.map((s) => ({
  //     id: s.SegmentId,
  //     start: parseInt(s.StartSeconds * 1000),
  //     length: parseInt(s.Length * 1000),
  //     end: parseInt(
  //       s.StartSeconds < 0
  //         ? totalTimeRef.current + (s.StartSeconds + s.Length) * 1000
  //         : (s.StartSeconds + s.Length) * 1000
  //     ), //precalculated for current time
  //   }));

  //   console.log(`Loaded time segments:`, segmentsResult);
  //   setTimeSegments(segmentsResult);
  // };

  const popupHalfWidth = 80 + 4; //width + padding
  const handleMouseMove = (e) => {
    if (!progressBarRef.current) return;
    const rect = progressBarRef.current.getBoundingClientRect();

    if (rect.left <= 0) {
      return; //out of left bounds
    }
    const x = e.clientX - rect.left; // x position within the element.
    const hoveredTime = (x / rect.width) * duration;
    const screenWidth = window.innerWidth || document.documentElement.clientWidth || document.body.clientWidth;

    let newPopupX = e.clientX - popupHalfWidth;
    // Clamp to left side
    if (newPopupX < 0) {
      newPopupX = 0;
    }

    // Clamp to right side
    if (newPopupX + popupHalfWidth * 2 > screenWidth) {
      newPopupX = screenWidth - popupHalfWidth * 2;
    }

    setPopupVisible(true);
    setPopupTime(timeFormat(hoveredTime));
    setPopupX(newPopupX);
    setLineX(e.clientX - 10); // 10px is container margin

    thumbnailRequest(hoveredTime);
  };

  const handleMouseLeave = () => {
    setPopupVisible(false);
  };

  const getThumbnailTime = (hoveredTime) => {
    return Math.floor(hoveredTime / 10000) * 10000; // Round down to nearest 10-second multiple
  };

  function timeFormat(durationMs: number) {
    if (durationMs <= 0) return '?';

    const durationSeconds = durationMs / 1000;
    // Hours, minutes and seconds
    const hrs = ~~(durationSeconds / 3600);
    const mins = ~~((durationSeconds % 3600) / 60);
    const secs = ~~durationSeconds % 60;

    // Output like "1:01" or "4:03:59" or "123:03:59"
    let ret = '';
    if (hrs > 0) {
      ret += '' + hrs + ':' + (mins < 10 ? '0' : '');
    }
    ret += '' + mins + ':' + (secs < 10 ? '0' : '');
    ret += '' + secs;

    return ret;
  }

  const lastRequestedTime = useRef<number>(null);
  const latestRequestId = useRef<number>(null);
  const thumbnailRequest = async (hoveredTime) => {
    const currentThumbTime = getThumbnailTime(hoveredTime);
    if (lastRequestedTime.current === currentThumbTime) {
      return;
    }

    const requestId = Date.now();
    latestRequestId.current = requestId;

    setIsLoading(true);
    lastRequestedTime.current = currentThumbTime;

    const currentKey = getThumbnailKey(currentThumbTime);
    if (thumbnailFiles[currentKey]) {
      setThumbnail(thumbnailFiles[currentKey]);
      setIsLoading(false);
      console.log('Grabbing thumb from cache:', currentThumbTime);
      return;
    }

    console.log('Requesting thumb:', currentThumbTime);
    debouncedThumbnailRequest(currentThumbTime);

    if (latestRequestId.current !== requestId) {
      // This request is outdated, ignore it.
      return;
    }
  };
  const debouncedThumbnailRequest = useRef(
    debounce((time) => {
      if (!client) return;
      client.sendEvent(SocketRequests.ThumbnailFile, { fileId, timeSeconds: time / 1000 });
    }, 100)
  ).current; //Debounced 100ms

  // const clearThumbCache = () => {
  //   if (blobUrlMap?.size > 0) {
  //     blobUrlMap.forEach((url) => URL.revokeObjectURL(url));
  //   }
  //   setBlobUrlMap(new Map());
  // };

  const getSegmentStyle = (segment) => {
    let startPercent, endPercent;

    if (segment.start < 0) {
      // Calculate start from end of file
      startPercent = ((duration + segment.start) / duration) * 100;
      endPercent = ((duration + segment.start + segment.length) / duration) * 100;
    } else {
      // Normal calculation for positive start
      startPercent = (segment.start / duration) * 100;
      endPercent = ((segment.start + segment.length) / duration) * 100;
    }

    const widthPercent = endPercent - startPercent;

    return {
      left: `${startPercent.toFixed(2)}%`,
      width: `${widthPercent.toFixed(2)}%`,
    };
  };

  // Handling skipping from SkipButton
  const handleSkip = () => {
    // ssetSkipRequested(true);
  };
  // useEffect(() => {
  //   // Have to force useEffect, because of 'Cannot update a component ProgressBar while rendering a different component SkipButton'
  //   if (skipRequested) {
  //     handleSliderChange(null, currentSkipTimeEnd);
  //     setSkipRequested(false);
  //   }
  // }, [skipRequested, currentSkipTimeEnd]);

  return (
    <div className="progress-controls">
      {popupVisible && (
        <div className="time-seek-popup" style={{ marginLeft: `${popupX}px` }}>
          <div className="popup-image">
            {isLoading ? <div className="spinner"></div> : thumbnail && <img src={thumbnail} alt="Thumbnail" />}
          </div>
          <div>{popupTime}</div>
        </div>
      )}
      <div className="skip-button-container">
        <SkipButton text={'Skip intro'} duration={10} isVisible={introButtonVisible} onSkip={handleSkip} />
        <SkipButton text={'Skip outro'} duration={10} isVisible={outroButtonVisible} onSkip={handleSkip} />
      </div>
      <div className="progress-slider-row">
        <div
          className="progress-slider-container"
          ref={progressBarRef}
          onMouseMove={handleMouseMove}
          onMouseOut={handleMouseLeave}
        >
          {timeSegments.map((segment, index) => (
            <div key={index} className="time-segment" style={getSegmentStyle(segment)}></div>
          ))}
          <Slider
            className="progress-slider"
            color="red"
            showLabelOnHover={false}
            label={null}
            defaultValue={40}
            min={0}
            max={duration}
            step={1000}
            value={currentTime}
            onChange={handleSliderChange}
          />
          {popupVisible && <div className="time-seek-line" style={{ left: `${lineX}px` }}></div>}
        </div>
        <div id="time-remaining">{timeFormat(duration - currentTime)}</div>
      </div>
    </div>
  );
};

export default ProgressBar;
