import React, { useEffect, useState } from 'react';
import { registerEventHandler } from '../../utils/events';
import '../Styles/ErrorOverlay.css';

const ErrorOverlay = () => {
  const [errors, setErrors] = useState<any[]>([]);

  useEffect(() => {
    registerEventHandler('player_error', addError);
  }, []);

  const addError = (error: string) => {
    setErrors((prevErrors) => [...prevErrors, error]);
  };

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
          <div className="error-container"></div>
          {renderError(error)}
          <button onClick={() => removeError(index)}>X</button>
        </div>
      ))}
    </div>
  );
};

export default ErrorOverlay;
