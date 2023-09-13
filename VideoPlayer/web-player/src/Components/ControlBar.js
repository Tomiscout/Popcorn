import React, { useRef } from 'react';
import ProgressBar from './ProgressBar';
import PlaybackControls from './PlaybackControls';
import { useEventListeners } from '../Utlis/helpers';

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
        }, 3000 * 600);
    };

    useEventListeners('keyup', { 'default': displayControls });
    useEventListeners('mousemove', { 'default': displayControls });

    return (
        <div ref={controlsContainerRef} className="controls-container">
            <ProgressBar />
            <PlaybackControls />
        </div>
    );
};

export default ControlBar;
