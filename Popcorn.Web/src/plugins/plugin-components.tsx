import { Component, ComponentType, ReactNode } from 'react';
import type { DetailsCardProps, DetailsProps, FileCardProps, SearchCardProps, WithDefault } from '@popcorn/plugin-sdk';
import { usePluginUi } from './plugins.hook';
import { DefaultDetails, DefaultDetailsCard, DefaultFileCard, DefaultSearchCard } from './default-components';

/**
 * Plugin UI slots: render the plugin's own component when its UI module exports one, else the built-in one.
 * Nothing is rendered until the module is loaded (no built-in -> custom flash).
 */

class PluginErrorBoundary extends Component<{ pluginId: string; fallback: ReactNode; children: ReactNode }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  componentDidCatch(error: unknown) {
    console.error(`Plugin ${this.props.pluginId} component failed, using the built-in one`, error);
  }

  render() {
    return this.state.failed ? this.props.fallback : this.props.children;
  }
}

function Slot<P extends { pluginId: string }>({
  props,
  Custom,
  Default,
}: {
  props: P;
  Custom: ComponentType<WithDefault<P>> | undefined;
  Default: ComponentType<P>;
}) {
  if (!Custom) return <Default {...props} />;
  return (
    <PluginErrorBoundary pluginId={props.pluginId} fallback={<Default {...props} />}>
      <Custom {...props} Default={Default} />
    </PluginErrorBoundary>
  );
}

export function PluginSearchCard(props: SearchCardProps) {
  const ui = usePluginUi(props.pluginId);
  return ui && <Slot props={props} Custom={ui.SearchCard} Default={DefaultSearchCard} />;
}

export function PluginDetailsCard(props: DetailsCardProps) {
  const ui = usePluginUi(props.pluginId);
  return ui && <Slot props={props} Custom={ui.DetailsCard} Default={DefaultDetailsCard} />;
}

export function PluginDetails(props: DetailsProps) {
  const ui = usePluginUi(props.pluginId);
  return ui && <Slot props={props} Custom={ui.Details} Default={DefaultDetails} />;
}

export function PluginFileCard(props: FileCardProps) {
  const ui = usePluginUi(props.pluginId);
  return ui && <Slot props={props} Custom={ui.FileCard} Default={DefaultFileCard} />;
}
