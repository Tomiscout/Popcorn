import React, { useEffect, useState } from 'react';
import { registerEventHandler, postMessage } from '../events';
import '../Styles/ProgressBar.css'

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
    const [popupContent, setPopupContent] = useState('');
    const [popupX, setPopupX] = useState(0);
    const [lineX, setLineX] = useState(0);

    const [currentTime, setCurrentTime] = useState(0);
    const [totalTime, setTotalTime] = useState(0);

    useEffect(() => {
        registerEventHandler('current_time', (data) => setCurrentTime(data));
        registerEventHandler('duration_update', (data) => setTotalTime(data));
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

    const popupHalfWidth = 30;
    const handleMouseMove = (e) => {
        const rect = e.target.getBoundingClientRect();
        if (rect.left <= 0) {
            return; //out of left bounds
        }
        const x = e.clientX - rect.left; // x position within the element.
        const hoveredTime = (x / rect.width) * totalTime;

        let newPopupX = e.clientX - popupHalfWidth;
        if (newPopupX < 0) { //clamp
            newPopupX = 0;
        }

        setPopupVisible(true);
        setPopupContent(timeFormat(hoveredTime));
        setPopupX(newPopupX);
        setLineX(e.clientX);
    };

    const handleMouseLeave = () => {
        setPopupVisible(false);
    };

    return (
        <div className="progress-controls">
            <div className='progress-slider-container'
                onMouseMove={handleMouseMove}
                onMouseOut={handleMouseLeave}>

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
                {popupVisible &&
                    <div className='time-seek-popup' style={{ left: `${popupX}px` }}>
                        {popupContent}
                    </div>
                }
            </div>
            <div id="time-remaining">
                {timeFormat(totalTime - currentTime)}
            </div>
        </div>
    );
};

export default ProgressBar;