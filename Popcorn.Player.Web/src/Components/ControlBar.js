import React, { useRef } from 'react';
import ProgressBar from './ProgressBar';
import PlaybackControls from './PlaybackControls';
import { useEventListeners } from '../Utlis/helpers';
import '../Styles/ControlBar.css';

const ControlBar = () => {
    const controlsTimeoutRef = useRef(null);
    const controlsContainerRef = useRef(null);

    const displayControls = () => {
        controlsContainerRef.current.style.opacity = '1';
        document.body.style.cursor = 'initial';

        if (controlsTimeoutRef.current) {
            clearTimeout(controlsTimeoutRef.current);
        }

        controlsTimeoutRef.current = setTimeout(() => {
            controlsContainerRef.current.style.opacity = '0';
            document.body.style.cursor = 'none';
        }, 3000);
    };

    useEventListeners('keyup', { 'default': displayControls });
    useEventListeners('mousemove', { 'default': displayControls });

    return (
        <div ref={controlsContainerRef} className='controls-root' style={{display: 'contents'}}>
            <div className="fading-background" />
            <div className="controls-container">
                <ProgressBar />
                <PlaybackControls />
            </div>
        </div>
    );
};

export default ControlBar;
