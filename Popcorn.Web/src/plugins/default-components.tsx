import {
  Accordion,
  Badge,
  Box,
  Button,
  Container,
  Flex,
  Group,
  Image,
  Loader,
  Menu,
  ScrollArea,
  Stack,
  Table,
  Text,
  Title,
  Tooltip,
} from '@mantine/core';
import { IconChevronDown, IconDownload, IconExternalLink, IconPlus } from '@tabler/icons-react';
import { useCallback, useState } from 'react';
import type {
  DetailsCardProps,
  DetailsProps,
  FileCardProps,
  FileLink,
  FileLinkKind,
  MediaBadge,
  MediaCard,
  MediaCardSection,
  SearchCardProps,
} from '@popcorn/plugin-sdk';
import { PluginDetailsCard } from './plugin-components';
import './default-components.css';

/** Built-in renderers of the plugin data model, used when a plugin has no UI module (or does not override one). */

const FALLBACK_IMAGE = '/images/default-media.jpg';

function Badges({ badges, size }: { badges: MediaBadge[]; size?: 'sm' }) {
  return (
    <Group gap={size ? 4 : 'xs'}>
      {badges.map((b, i) => (
        <Badge key={`${b.label}-${i}`} size={size} color={b.color ?? undefined} variant={b.color ? 'filled' : 'light'}>
          {b.label}
        </Badge>
      ))}
    </Group>
  );
}

export function DefaultSearchCard({ card, onOpen }: SearchCardProps) {
  return (
    <Flex
      direction="row"
      gap="md"
      p="sm"
      className={onOpen ? 'plugin-card plugin-card--clickable' : 'plugin-card'}
      onClick={onOpen}
    >
      <Image
        src={card.imageUrl || FALLBACK_IMAGE}
        fallbackSrc={FALLBACK_IMAGE}
        alt={card.title}
        w={170}
        h={255}
        fit="cover"
        radius="sm"
        style={{ flexShrink: 0 }}
      />
      <Stack gap="sm">
        <Title order={2}>{card.title}</Title>
        <Group gap="sm">
          {card.subtitle && <Text size="md">{card.subtitle}</Text>}
          <Badges badges={card.badges} />
        </Group>
        {card.description && (
          <Text size="sm" lineClamp={8}>
            {card.description}
          </Text>
        )}
      </Stack>
    </Flex>
  );
}

export function DefaultDetailsCard({ card, onOpen }: DetailsCardProps) {
  return (
    <Stack
      gap={4}
      w={150}
      className={onOpen ? 'plugin-small-card plugin-card--clickable' : 'plugin-small-card'}
      onClick={onOpen}
    >
      <Image
        src={card.imageUrl || FALLBACK_IMAGE}
        fallbackSrc={FALLBACK_IMAGE}
        alt={card.title}
        w={150}
        h={225}
        fit="cover"
        radius="sm"
      />
      <Text size="sm" fw={600} lineClamp={2}>
        {card.title}
      </Text>
      {card.subtitle && (
        <Text size="xs" c="dimmed" lineClamp={1}>
          {card.subtitle}
        </Text>
      )}
    </Stack>
  );
}

const LINK_LABELS: Record<FileLinkKind, string> = {
  Magnet: 'Magnet',
  Torrent: '.torrent',
  Direct: 'Download',
  Page: 'Page',
};

const EXTERNAL_LABELS: Record<FileLinkKind, string> = {
  Magnet: 'Open in torrent program',
  Torrent: 'Open in torrent program',
  Direct: 'Open in browser',
  Page: 'Open page',
};

const PILL_LABELS: Partial<Record<FileLinkKind, string>> = {
  Magnet: '🧲 Magnet',
  Torrent: '.torrent',
};

/**
 * Keys of the card's actions that are still running (a `resolve` link being fetched): `run` marks the key busy while
 * the promise `work` returns is pending; synchronous actions never show as busy.
 */
function useBusy() {
  const [busy, setBusy] = useState<ReadonlySet<string>>(() => new Set());
  const run = useCallback((key: string, work: () => void | Promise<void>) => {
    const result = work();
    if (!result) return;
    setBusy((b) => new Set(b).add(key));
    void result.finally(() =>
      setBusy((b) => {
        const next = new Set(b);
        next.delete(key);
        return next;
      })
    );
  }, []);
  return { busy, run };
}

/** Menu of a downloadable link: Popcorn's own client or the external one (the default first). */
function LinkMenuItems({ link, openLink }: { link: FileLink; openLink: FileCardProps['openLink'] }) {
  const internalLabel = link.kind === 'Direct' ? 'Download in Popcorn' : 'Download with Popcorn torrent client';
  return (
    <>
      <Menu.Item leftSection={<IconDownload size={14} />} onClick={() => openLink(link, 'internal')}>
        {internalLabel}
      </Menu.Item>
      <Menu.Item leftSection={<IconExternalLink size={14} />} onClick={() => openLink(link, 'external')}>
        {EXTERNAL_LABELS[link.kind]}
      </Menu.Item>
    </>
  );
}

/** Magnet / .torrent link as a small pill: secondary to Add, opens a menu to pick the client. */
function LinkPill({ link, openLink, busy }: { link: FileLink; openLink: FileCardProps['openLink']; busy: boolean }) {
  const label = link.label ?? PILL_LABELS[link.kind] ?? link.kind;
  return (
    <Menu position="bottom-end" withinPortal disabled={busy}>
      <Menu.Target>
        <Badge
          component="button"
          type="button"
          variant="outline"
          color="gray"
          size="lg"
          radius="xl"
          disabled={busy}
          aria-busy={busy}
          leftSection={busy ? <Loader size={12} color="gray" /> : undefined}
          style={{ cursor: busy ? 'wait' : 'pointer', textTransform: 'none', fontWeight: 500 }}
        >
          {label}
        </Badge>
      </Menu.Target>
      <Menu.Dropdown>
        <LinkMenuItems link={link} openLink={openLink} />
      </Menu.Dropdown>
    </Menu>
  );
}

/** Button of a link; downloadable links get a menu to pick Popcorn's own client or the external one. */
function FileLinkButton({
  link,
  openLink,
  defaultTarget,
  busy,
}: {
  link: FileLink;
  openLink: FileCardProps['openLink'];
  defaultTarget: FileCardProps['defaultTarget'];
  busy: boolean;
}) {
  const label = link.label ?? LINK_LABELS[link.kind] ?? link.kind;
  const target = defaultTarget(link.kind);
  if (!target)
    return (
      <Button size="compact-sm" variant="subtle" loading={busy} onClick={() => openLink(link)}>
        {label}
      </Button>
    );

  const internalLabel = link.kind === 'Direct' ? 'Download in Popcorn' : 'Download with Popcorn torrent client';
  return (
    <Button.Group>
      <Tooltip label={target === 'internal' ? internalLabel : EXTERNAL_LABELS[link.kind]} openDelay={400}>
        <Button
          size="compact-sm"
          variant="light"
          leftSection={target === 'internal' ? <IconDownload size={14} /> : <IconExternalLink size={14} />}
          loading={busy}
          onClick={() => openLink(link)}
        >
          {label}
        </Button>
      </Tooltip>
      <Menu position="bottom-end" withinPortal>
        <Menu.Target>
          <Button size="compact-sm" variant="light" px={4} disabled={busy} aria-label={`${label}: download options`}>
            <IconChevronDown size={14} />
          </Button>
        </Menu.Target>
        <Menu.Dropdown>
          <LinkMenuItems link={link} openLink={openLink} />
        </Menu.Dropdown>
      </Menu>
    </Button.Group>
  );
}

export function DefaultFileCard({ file, openLink, defaultTarget, add }: FileCardProps) {
  const { busy, run } = useBusy();
  return (
    <Group className="plugin-file-card" p="sm" gap="sm" justify="space-between" wrap="nowrap" align="flex-start">
      <Stack gap={4} style={{ minWidth: 0 }}>
        <Text fw={600} style={{ overflowWrap: 'anywhere' }}>
          {file.title}
        </Text>
        {file.subtitle && (
          <Text size="xs" c="dimmed">
            {file.subtitle}
          </Text>
        )}
        {file.description && (
          <Text size="xs" lineClamp={2}>
            {file.description}
          </Text>
        )}
        {file.badges.length > 0 && <Badges badges={file.badges} size="sm" />}
      </Stack>
      <Group gap="xs" wrap="nowrap" style={{ flexShrink: 0 }}>
        {file.links.map((link, i) => {
          const key = `${link.kind}-${i}`;
          const open: FileCardProps['openLink'] = (l, target) => run(key, () => openLink(l, target));
          return link.kind === 'Magnet' || link.kind === 'Torrent' ? (
            <LinkPill key={key} link={link} openLink={open} busy={busy.has(key)} />
          ) : (
            <FileLinkButton key={key} link={link} openLink={open} defaultTarget={defaultTarget} busy={busy.has(key)} />
          );
        })}
        {add && (
          <Tooltip label={add.hint} openDelay={400}>
            <Button
              size="compact-sm"
              leftSection={<IconPlus size={14} />}
              loading={busy.has('add')}
              onClick={() => run('add', add.onAdd)}
            >
              Add
            </Button>
          </Tooltip>
        )}
      </Group>
    </Group>
  );
}

/** Large image, fades in once loaded instead of popping in. Keyed by src so a new image starts hidden again. */
function Backdrop({ src }: { src: string }) {
  const [loaded, setLoaded] = useState(false);
  return (
    <img
      src={src}
      alt=""
      className="plugin-details-backdrop"
      data-loaded={loaded || undefined}
      onLoad={() => setLoaded(true)}
    />
  );
}

function SectionCards({
  pluginId,
  section,
  onOpenCard,
}: {
  pluginId: string;
  section: MediaCardSection;
  onOpenCard: (card: MediaCard) => void;
}) {
  return (
    <ScrollArea type="auto" offsetScrollbars>
      <Group gap="md" wrap="nowrap" align="flex-start">
        {section.cards.map((card) => (
          <PluginDetailsCard
            key={card.id}
            pluginId={pluginId}
            card={card}
            onOpen={card.hasDetails ? () => onOpenCard(card) : undefined}
          />
        ))}
      </Group>
    </ScrollArea>
  );
}

export function DefaultDetails({ pluginId, details, onOpenCard }: DetailsProps) {
  return (
    <div>
      <Box pos="relative" style={{ overflow: 'hidden' }}>
        {details.backdropUrl && <Backdrop key={details.backdropUrl} src={details.backdropUrl} />}
        <div className="plugin-details-shade" />
        <Container size="lg" style={{ position: 'relative', paddingTop: 72, paddingBottom: 64 }}>
          <Group align="flex-start" gap="xl" wrap="nowrap">
            {details.posterUrl && (
              <Image
                src={details.posterUrl}
                w={300}
                radius="md"
                style={{ flexShrink: 0, boxShadow: '0 20px 40px rgba(0,0,0,0.5)' }}
              />
            )}
            <Stack gap="sm" maw={640}>
              <Title order={1} c="white">
                {details.title}
              </Title>
              <Badges badges={details.badges} />
              {details.tagline && (
                <Text c="gray.2" size="lg" fs="italic">
                  {details.tagline}
                </Text>
              )}
              {details.overview && (
                <Text c="gray.3" size="lg">
                  {details.overview}
                </Text>
              )}
              {details.fields.length > 0 && (
                <Table withRowBorders={false} verticalSpacing={4} w="auto">
                  <Table.Tbody>
                    {details.fields.map((f, i) => (
                      <Table.Tr key={`${f.label}-${i}`}>
                        <Table.Td c="dimmed" pl={0} style={{ whiteSpace: 'nowrap', verticalAlign: 'top' }}>
                          {f.label}
                        </Table.Td>
                        <Table.Td c="gray.2">{f.value}</Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              )}
            </Stack>
          </Group>
        </Container>
      </Box>

      <Container size="lg" py="md">
        {details.sections.map((section, i) =>
          section.collapsed ? (
            <Accordion key={`${section.title}-${i}`} chevronPosition="left" mb="lg">
              <Accordion.Item value="section">
                <Accordion.Control px={0}>
                  <Title order={3}>{section.title}</Title>
                </Accordion.Control>
                <Accordion.Panel>
                  <SectionCards pluginId={pluginId} section={section} onOpenCard={onOpenCard} />
                </Accordion.Panel>
              </Accordion.Item>
            </Accordion>
          ) : (
            <Box key={`${section.title}-${i}`} mb="lg">
              <Title order={3} mb="sm">
                {section.title}
              </Title>
              <SectionCards pluginId={pluginId} section={section} onOpenCard={onOpenCard} />
            </Box>
          )
        )}
      </Container>
    </div>
  );
}
