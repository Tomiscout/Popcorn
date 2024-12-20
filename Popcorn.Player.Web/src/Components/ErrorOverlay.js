import React, { useEffect, useState } from 'react';
import { registerWebviewEventHandler } from '../events'
import '../Styles/ErrorOverlay.css';

const ErrorOverlay = () => {
    const [errors, setErrors] = useState([]);

    useEffect(() => {
        registerWebviewEventHandler('error', addError);
    }, []);

    const addError = (error) => {
        setErrors((prevErrors) => [...prevErrors, error]);
    };

    const removeError = (index) => {
        setErrors((prevErrors) => prevErrors.filter((_, i) => i !== index));
    };

    return (
        <div className="error-overlay">
            {errors.map((error, index) => (
                <div key={index} className="error-message">
                    <div className="error-container">
                        <span className="error-message-text">{error.message}</span>
                        <span className="error-message-exception-text">Exception: {error.exceptionMessage}</span>
                        {error.stacktrace && <pre className="error-message-stacktrace">{error.stacktrace}</pre>}

                        {error.innerMessage && <span style={{marginTop:'0.7rem'}} className="error-message-exception-text">Inner exception: {error.innerMessage}</span>}
                        {error.innerStacktrace && <pre className="error-message-stacktrace">{error.innerStacktrace}</pre>}
                    </div>


                    <button onClick={() => removeError(index)}>X</button>
                </div>
            ))}
        </div>
    );
};

export default ErrorOverlay;