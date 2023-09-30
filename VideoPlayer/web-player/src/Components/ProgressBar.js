import React, { useEffect, useState, useRef } from 'react';
import { registerEventHandler, registerSharedBufferHandler, postMessage } from '../events';
import '../Styles/ProgressBar.css'
import debounce from 'lodash.debounce';

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
    const [totalTime, setTotalTime] = useState(0);

    useEffect(() => {
        registerEventHandler('current_time', (data) => setCurrentTime(data));
        registerEventHandler('duration_update', (data) => setTotalTime(data));

        registerSharedBufferHandler('thumb', thumbnailCallback);
    }, []);

    const progressClick = (event) => {
        setCurrentTime(event);
        postMessage('set_playback_time', event) //TODO throttle
    }

    const getBackgroundSize = () => {
        return {
            backgroundSize: `${(currentTime / totalTime * 100)}% 100%`,
        };
    };

    const popupHalfWidth = 80 + 4; //width + padding
    const handleMouseMove = (e) => {
        const rect = e.target.getBoundingClientRect();

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
        setLineX(e.clientX);

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
            <div className='progress-slider-row'
                onMouseMove={handleMouseMove}
                onMouseOut={handleMouseLeave}>
                <div className='progress-slider-container' ref={progressBarRef}>
                    <input
                        type="range"
                        min="0"
                        max={totalTime}
                        step="1000"
                        onChange={(e) => progressClick(e.target.value)}
                        style={getBackgroundSize()}
                        className='progress-slider'
                        value={currentTime}

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