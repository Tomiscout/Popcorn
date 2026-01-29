import { useState, useEffect } from 'react';
import { IconPlus, IconMinus } from '@tabler/icons-react';

import './NumberStepper.css';
interface NumberStepperProps {
  onNumberChanged: (val: number) => void;
  step: number;
  reset: boolean;
  initialValue: number;
}

const NumberStepper: React.FC<NumberStepperProps> = ({ onNumberChanged, step, reset, initialValue = 0.0 }) => {
  const [value, setValue] = useState(initialValue);

  useEffect(() => {
    if (reset) {
      setValue(0);
      onNumberChanged(0);
    }
  }, [reset]);

  const handleValueChange = (delta: number) => {
    const newValue = value + delta;
    setValue(newValue);
    onNumberChanged(newValue);
  };

  return (
    <div className="number-stepper">
      <button onClick={() => handleValueChange(-step)}>
        <IconMinus />
      </button>
      <span>{(value === 0 ? 0 : value).toFixed(2)} s</span>
      <button onClick={() => handleValueChange(step)}>
        <IconPlus />
      </button>
    </div>
  );
};

export default NumberStepper;
