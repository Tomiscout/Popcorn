import { Anchor, Avatar, Box, Container, Group, ScrollArea, Stack, Text, Title } from '@mantine/core';
import type { DetailsProps, WithDefault } from '@popcorn/plugin-sdk';

/** MediaDetails.Data of the TMDb plugin (TmdbDetailsData in TmdbPlugin.cs). */
interface TmdbDetailsData {
  cast: { name: string | null; character: string | null; profileUrl: string | null }[];
  imdbUrl: string | null;
  tmdbUrl: string;
}

/** Built-in details page + cast row + external links. */
export function Details(props: WithDefault<DetailsProps<TmdbDetailsData>>) {
  const { Default, ...rest } = props;
  const data = props.details.data;

  return (
    <>
      <Default {...rest} />
      {data && (
        <Container size="lg" pb="xl">
          {data.cast.length > 0 && (
            <Box mb="lg">
              <Title order={3} mb="sm">
                Cast
              </Title>
              <ScrollArea type="auto" offsetScrollbars>
                <Group gap="lg" wrap="nowrap" align="flex-start">
                  {data.cast.map((c, i) => (
                    <Stack key={i} gap={4} align="center" w={110} style={{ flexShrink: 0 }}>
                      <Avatar src={c.profileUrl} name={c.name ?? undefined} size={96} color="initials" />
                      <Text size="sm" fw={600} ta="center" lineClamp={2}>
                        {c.name}
                      </Text>
                      <Text size="xs" c="dimmed" ta="center" lineClamp={2}>
                        {c.character}
                      </Text>
                    </Stack>
                  ))}
                </Group>
              </ScrollArea>
            </Box>
          )}
          <Group gap="lg">
            <Anchor href={data.tmdbUrl} target="_blank" rel="noreferrer">
              View on TMDb
            </Anchor>
            {data.imdbUrl && (
              <Anchor href={data.imdbUrl} target="_blank" rel="noreferrer">
                View on IMDb
              </Anchor>
            )}
          </Group>
        </Container>
      )}
    </>
  );
}
