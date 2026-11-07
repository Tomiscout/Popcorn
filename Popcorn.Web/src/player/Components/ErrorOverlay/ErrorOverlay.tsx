import { useEffect, useState } from 'react';
import { registerEventHandler, unregisterEventHandler } from '../../../utils/events';
import './ErrorOverlay.css';

const ErrorOverlay = () => {
  const [errors, setErrors] = useState<any[]>([]);

  useEffect(() => {
    const addError = (error: unknown) => {
      setErrors((prevErrors) => [...prevErrors, error]);
    };
    registerEventHandler('player_error', addError);
    return () => unregisterEventHandler('player_error', addError);
  }, []);

  const removeError = (index: number) => {
    setErrors((prevErrors) => prevErrors.filter((_, i) => i !== index));
  };

  function renderError(error: any) {
    if (typeof error === 'string') {
      return <span className="error-message-text">{error}</span>;
    }

    return (
      <>
        <span className="error-message-text">{error.message}</span>
        <span className="error-message-exception-text">Exception: {error.exceptionMessage}</span>
        {error.stacktrace && <pre className="error-message-stacktrace">{error.stacktrace}</pre>}

        {error.innerMessage && (
          <span style={{ marginTop: '0.7rem' }} className="error-message-exception-text">
            Inner exception: {error.innerMessage}
          </span>
        )}
        {error.innerStacktrace && <pre className="error-message-stacktrace">{error.innerStacktrace}</pre>}
      </>
    );
  }

  return (
    <div className="error-overlay">
      {errors.map((error, index) => (
        <div key={index} className="error-message">
          <div className="error-container">{renderError(error)}</div>
          <button onClick={() => removeError(index)}>X</button>
        </div>
      ))}
    </div>
  );
};

export default ErrorOverlay;
