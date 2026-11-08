import { IconPlus, IconMinus } from '@tabler/icons-react';

import './NumberStepper.css';
interface NumberStepperProps {
  value: number;
  onChange: (val: number) => void;
  step: number;
  format?: (val: number) => string;
}

const NumberStepper: React.FC<NumberStepperProps> = ({ value, onChange, step, format }) => (
  <div className="number-stepper">
    <button onClick={() => onChange(value - step)}>
      <IconMinus />
    </button>
    <span>{format ? format(value) : `${(value === 0 ? 0 : value).toFixed(2)} s`}</span>
    <button onClick={() => onChange(value + step)}>
      <IconPlus />
    </button>
  </div>
);

export default NumberStepper;
