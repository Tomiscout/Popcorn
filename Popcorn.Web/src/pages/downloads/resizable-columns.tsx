import { Table } from '@mantine/core';
import React from 'react';

/** Header cell with a drag handle on its right edge (double click = default width). */
export function ResizableTh({
  width,
  onResize,
  onReset,
  children,
}: {
  width: number;
  onResize: (width: number) => void;
  onReset: () => void;
  children?: React.ReactNode;
}) {
  const startDrag = (e: React.PointerEvent<HTMLDivElement>) => {
    e.preventDefault();
    const handle = e.currentTarget;
    const startX = e.clientX;
    handle.setPointerCapture(e.pointerId);
    const move = (ev: PointerEvent) => onResize(width + ev.clientX - startX);
    const up = () => {
      handle.removeEventListener('pointermove', move);
      handle.removeEventListener('pointerup', up);
      handle.removeEventListener('pointercancel', up);
    };
    handle.addEventListener('pointermove', move);
    handle.addEventListener('pointerup', up);
    handle.addEventListener('pointercancel', up);
  };

  return (
    <Table.Th style={{ position: 'relative' }}>
      {children}
      <div
        role="separator"
        aria-orientation="vertical"
        title="Drag to resize, double click to reset"
        onPointerDown={startDrag}
        onDoubleClick={onReset}
        style={{
          position: 'absolute',
          top: 0,
          right: -4,
          width: 8,
          height: '100%',
          cursor: 'col-resize',
          zIndex: 1,
          touchAction: 'none',
          borderRight: '1px solid var(--mantine-color-default-border)',
          boxSizing: 'border-box',
          marginRight: 4,
        }}
      />
    </Table.Th>
  );
}
