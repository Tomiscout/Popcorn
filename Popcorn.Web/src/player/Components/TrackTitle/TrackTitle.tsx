import { usePlayerStore } from '../../../state/player.store';

export const TrackTitle = () => {
  const { metadata } = usePlayerStore();

  return (
    <div style={{ flex: 1 }}>
      {metadata?.isTvShow ? (
        <div id="seriesTitle" className="title">
          <span className="series">{metadata.season}</span> <span className="episode">{metadata.episode}</span>
        </div>
      ) : (
        <div className="title">
          <span id="plainTitleSpan" className="series">
            {metadata?.shortTitle}
          </span>
        </div>
      )}
    </div>
  );
};
