import React, { useEffect, useState, useRef } from 'react';
import { registerWebviewEventHandler, registerSharedBufferHandler, postMessage } from '../events';
import '../Styles/ProgressBar.css'
import debounce from 'lodash.debounce';
import Slider from '@mui/material/Slider';
import SkipButton from './SkipButton';

function timeFormat(duration) {
    duration = duration / 1000;
    // Hours, minutes and seconds
    const hrs = ~~(duration / 3600);
    const mins = ~~((duration % 3600) / 60);
    const secs = ~~duration % 60;

    // Output like "1:01" or "4:03:59" or "123:03:59"
    let ret = "";
    if (hrs > 0) {
        ret += "" + hrs + ":" + (mins < 10 ? "0" : "");
    }
    ret += "" + mins + ":" + (secs < 10 ? "0" : "");
    ret += "" + secs;

    return ret;
}

const ProgressBar = () => {
    const [popupVisible, setPopupVisible] = useState(false);
    const [popupTime, setPopupTime] = useState('');
    const [popupX, setPopupX] = useState(0);
    const [lineX, setLineX] = useState(0);
    const progressBarRef = useRef(null);
    const [thumbnail, setThumbnail] = useState(null);
    const [isLoading, setIsLoading] = useState(false);

    const [currentTime, setCurrentTime] = useState(0);
    const [totalTime, setTotalTime] = useState(10000);
    const totalTimeRef = useRef(totalTime); //Needed for segments calculation and immediate access to value. could be avoided with precalculated end from backend
    const [timeSegments, setTimeSegments] = useState([]);
    const timeSegmentsRef = useRef(timeSegments); //withou using ref - handleCurrentTime closure cant access newest segments, just default []
    const [introButtonVisible, setIntroButtonVisible] = useState(false);
    const [outroButtonVisible, setOutroButtonVisible] = useState(false);
    const introButtonVisibleRef = useRef(introButtonVisible);
    const outroButtonVisibleRef = useRef(outroButtonVisible);

    const [skipRequested, setSkipRequested] = useState(false);
    const [currentSkipTimeEnd, setCurrentSkipTimeEnd] = useState(0);
    const currentSkipTimeEndRef = useRef(currentSkipTimeEnd);

    useEffect(() => {
        registerWebviewEventHandler('current_time', (data) => handleCurrentTime(data));
        registerWebviewEventHandler('duration_update', (data) => setTotalTime(data));
        registerWebviewEventHandler('segments_update', (data) => setSegments(data));

        registerSharedBufferHandler('thumb', thumbnailCallback);
    }, []);

    useEffect(() => {
        totalTimeRef.current = totalTime;
    }, [totalTime]);

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

    const handleSliderChange = (event, newValue) => {
        setCurrentTime(newValue);
        postMessage('set_playback_time', newValue) //TODO throttle
    };

    const handleCurrentTime = (newTime) => {
        setCurrentTime(newTime);

        if(introButtonVisibleRef.current || outroButtonVisibleRef.current) {
            // Finds current segment and check if still in range
            for(const segment of timeSegmentsRef.current) {
                if(segment.end !== currentSkipTimeEndRef.current) continue;
                
                if(newTime < segment.start || newTime > segment.end) {
                    setOutroButtonVisible(false);
                    setIntroButtonVisible(false);
                }
                return;
            }
            return;
        }

        for(const segment of timeSegmentsRef.current) {
            if(segment.start < 0) {
                if(segment.start + totalTimeRef.current <= newTime && segment.end > newTime){
                    setOutroButtonVisible(true);
                    setCurrentSkipTimeEnd(segment.end);
                    return;
                }
            } else if(segment.start <= newTime && segment.end > newTime) {
                setIntroButtonVisible(true);
                setCurrentSkipTimeEnd(segment.end);
                return;
            }
        }
    }

    const setSegments = (data) => {
        const newSegments = [];
        if (data.intro) {
            newSegments.push(...data.intro);
        }
        if (data.outro) {
            newSegments.push(...data.outro);
        }

        //Convert to ms, ugly but good for now
        const segmentsResult = newSegments.map(s => ({
            id: s.SegmentId,
            start: parseInt(s.StartSeconds * 1000),
            length: parseInt(s.Length * 1000),
            end: parseInt(s.StartSeconds < 0 ? totalTimeRef.current + (s.StartSeconds + s.Length) * 1000 : (s.StartSeconds + s.Length) * 1000) //precalculated for current time
        }));

        console.log(`Loaded time segments:`, segmentsResult);
        setTimeSegments(segmentsResult);
    };

    const popupHalfWidth = 80 + 4; //width + padding
    const handleMouseMove = (e) => {
        const rect = progressBarRef.current.getBoundingClientRect();

        if (rect.left <= 0) {
            return; //out of left bounds
        }
        const x = e.clientX - rect.left; // x position within the element.
        const hoveredTime = (x / rect.width) * totalTime;
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

    const getThumbnailTime = (hoveredTime) => {
        return Math.floor(hoveredTime / 10000) * 10000; // Round down to nearest 10-second multiple
    };

    const lastRequestedTime = useRef(null);
    const latestRequestId = useRef(null);
    const thumbnailRequest = async (hoveredTime) => {
        const currentThumbTime = getThumbnailTime(hoveredTime);
        if (lastRequestedTime.current === currentThumbTime) {
            return;
        }

        const requestId = Date.now();
        latestRequestId.current = requestId;

        setIsLoading(true);
        lastRequestedTime.current = currentThumbTime;

        console.log('Requesting thumb:', currentThumbTime);

        debouncedPostMessage(currentThumbTime);

        if (latestRequestId.current !== requestId) {
            // This request is outdated, ignore it.
            return;
        }
    };
    const debouncedPostMessage = useRef(debounce((time) => {
        postMessage('thumb_requested', time / 1000);
    }, 100)).current; //Debounced 100ms

    const thumbnailCallback = (sharedBuffer, additionalData) => {
        const blob = new Blob([sharedBuffer]);
        const imageUrl = URL.createObjectURL(blob);

        setThumbnail(imageUrl);
        setIsLoading(false);
    }

    const handleMouseLeave = () => {
        setPopupVisible(false);
    };

    const getSegmentStyle = (segment) => {
        let startPercent, endPercent;

        if (segment.start < 0) {
            // Calculate start from end of file
            startPercent = ((totalTime + segment.start) / totalTime) * 100;
            endPercent = ((totalTime + segment.start + segment.length) / totalTime) * 100;
        } else {
            // Normal calculation for positive start
            startPercent = (segment.start / totalTime) * 100;
            endPercent = ((segment.start + segment.length) / totalTime) * 100;
        }

        const widthPercent = endPercent - startPercent;

        return {
            left: `${startPercent.toFixed(2)}%`,
            width: `${widthPercent.toFixed(2)}%`
        };
    };

    // Handling skipping from SkipButton
    const handleSkip = () => {
        setSkipRequested(true);
    };
    useEffect(() => { // Have to force useEffect, because of 'Cannot update a component ProgressBar while rendering a different component SkipButton'
        if (skipRequested) {
            handleSliderChange(null, currentSkipTimeEnd);
            setSkipRequested(false);
        }
    }, [skipRequested, currentSkipTimeEnd]);

    return (
        <div className="progress-controls">
            {popupVisible &&
                <div className='time-seek-popup' style={{ marginLeft: `${popupX}px` }}>
                    <div className='popup-image'>
                        {isLoading ? (
                            <div className="spinner"></div>
                        ) : (
                            thumbnail && <img src={thumbnail} alt="Thumbnail" />
                        )}
                    </div>
                    <div>
                        {popupTime}
                    </div>
                </div>
            }
            <div className="skip-button-container">
                <SkipButton text={"Skip intro"} duration={10} isVisible={introButtonVisible} onSkip={handleSkip} />
                <SkipButton text={"Skip outro"} duration={10} isVisible={outroButtonVisible} onSkip={handleSkip} />
            </div>
            <div className='progress-slider-row'>
                <div className='progress-slider-container' ref={progressBarRef} onMouseMove={handleMouseMove}
                    onMouseOut={handleMouseLeave}>
                    {timeSegments.map((segment, index) => (
                        <div key={index} className="time-segment" style={getSegmentStyle(segment)}></div>
                    ))}
                    <Slider
                        className='progress-slider'
                        min={0}
                        max={totalTime}
                        value={currentTime}
                        onChange={handleSliderChange}
                        step={1000}
                        sx={{
                            color: '#E31221', // Sets the color of the thumb and the active part of the rail

                            '& .MuiSlider-rail, & .MuiSlider-track': {
                                height: '8px', // Increase rail and track height
                                border: 'none'
                            },

                            '& .MuiSlider-thumb': {
                                zIndex: 10,
                                boxShadow: 'none', // Remove default shadow

                                '&:hover, &.Mui-focusVisible': {
                                    boxShadow: 'none', // Remove shadow on hover and focus
                                },

                                '&.Mui-active': {
                                    boxShadow: '0px 0px 0px 8px rgba(227, 18, 33, 0.16)', // Shadow when pressed or dragging
                                }
                            }
                        }}
                    />
                    {popupVisible &&
                        <div className='time-seek-line' style={{ left: `${lineX}px` }}></div>
                    }

                </div>
                <div id="time-remaining">
                    {timeFormat(totalTime - currentTime)}
                </div>
            </div>
        </div>
    );
};

export default ProgressBar;