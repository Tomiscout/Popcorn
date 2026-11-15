import { NumberInput, NumberInputProps } from '@mantine/core';
import { clampAutoSkipDelay, MAX_AUTO_SKIP_DELAY } from '../model/segments.contract';

interface AutoSkipDelayInputProps extends Omit<NumberInputProps, 'value' | 'onChange' | 'defaultValue'> {
  /** Seconds (`SettingsModel.SegmentAutoSkipDelay`). */
  value: number;
  /** Called on blur / Enter with whole seconds in 0..99999, only when the value changed. */
  onChange: (seconds: number) => void;
}

/** "Auto-skip delay (seconds)": settings page and the player's settings menu. */
export const AutoSkipDelayInput = ({ value, onChange, ...props }: AutoSkipDelayInputProps) => {
  const commit = (raw: string) => {
    const seconds = clampAutoSkipDelay(raw.trim() === '' ? value : Number(raw));
    if (seconds !== value) onChange(seconds);
  };

  return (
    <NumberInput
      // Remounted when the saved value changes (e.g. from another client), like the download number fields.
      key={value}
      label="Auto-skip delay (seconds)"
      description="Count-down of the skip button before an intro or outro is skipped on its own; 0 skips it as soon as it starts."
      defaultValue={value}
      min={0}
      max={MAX_AUTO_SKIP_DELAY}
      allowNegative={false}
      allowDecimal={false}
      clampBehavior="strict"
      w={260}
      onBlur={(e) => commit(e.currentTarget.value)}
      onKeyDown={(e) => e.key === 'Enter' && commit(e.currentTarget.value)}
      {...props}
    />
  );
};
