import React, { useState, useEffect } from 'react';
import PropTypes from 'prop-types';
import { ReactComponent as PlusIcon } from '../assets/icons/plus.svg';
import { ReactComponent as MinusIcon } from '../assets/icons/minus.svg';

import '../Styles/NumberStepper.css';

const NumberStepper = ({ onNumberChanged, step, reset }) => {
  const [value, setValue] = useState(0.0);

  useEffect(() => {
    if (reset) {
      setValue(0);
      onNumberChanged(0);
    }
  }, [reset]);

  const handleValueChange = (delta) => {
    const newValue = value + delta;
    setValue(newValue);
    onNumberChanged(newValue);
  };

  return (
    <div className='number-stepper'>
      <button onClick={() => handleValueChange(-step)}>
        <MinusIcon />
      </button>
      <span>{value.toFixed(2)} s</span>
      <button onClick={() => handleValueChange(step)}>
        <PlusIcon />
      </button>
    </div>
  );
};

NumberStepper.propTypes = {
  onNumberChanged: PropTypes.func.isRequired,
  step: PropTypes.number.isRequired,
  reset: PropTypes.bool.isRequired,
};

export default NumberStepper;
