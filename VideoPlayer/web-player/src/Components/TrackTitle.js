import React, { useState, useEffect } from 'react';
import { registerWebviewEventHandler } from '../events';

const TrackTitle = () => {
    const [title, setTitle] = useState('');
    const [season, setSeason] = useState(0);
    const [episode, setEpisode] = useState(0);
    const [episodeTitle, setEpisodeTitle] = useState('');
    const [showSeriesTitle, setShowSeriesTitle] = useState(false);

    useEffect(() => {
        registerWebviewEventHandler('track_loaded', (data) => {
            if (data.title) {
                setTitle(data.shortTitle || data.title)
            } else {
                setSeason(data.series);
                setEpisode(data.episode);
                setEpisodeTitle(data.episodeTitle)
            }

            setShowSeriesTitle(!data.title);
        });
    }, []);

    return (
        <div style={{ flex: 1 }}>
            {!showSeriesTitle &&
                <div className="title" >
                    <span id="plainTitleSpan" className="series">{title}</span>
                </div>
            }
            {showSeriesTitle &&
                <div id="seriesTitle" class="title">
                    <span class="series">{season}</span> <span class="episode">{episode}</span>
                </div>
            }
        </div>
    );
};

export default TrackTitle;
