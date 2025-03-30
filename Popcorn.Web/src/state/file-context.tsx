import React, {
  createContext,
  useState,
  useCallback,
  useContext,
  useRef,
  useEffect,
  useMemo,
  PropsWithChildren,
} from 'react';

interface FileContextState {
  fileUrlsMap: Map<string, string>;
  addOrUpdateFileUrl: (filename: string, url: string) => void;
  getFilesToLoad: (filePaths: string[]) => string[];
  getFileUrl: (filename: string) => string | undefined;
}

const FileContext = createContext<FileContextState | undefined>(undefined);

/**
 * Provides a context for managing filename-to-objectURL mappings.
 * Handles asynchronous updates and cleans up generated Object URLs.
 */
export const FileProvider: React.FC<PropsWithChildren<{}>> = ({ children }) => {
  const [fileUrlsMap, setFileUrlsMap] = useState<Map<string, string>>(new Map());
  const createdUrlsRef = useRef<Set<string>>(new Set());

  const addOrUpdateFileUrl = useCallback((filename: string, url: string) => {
    createdUrlsRef.current.add(url);
    setFileUrlsMap((prevMap) => {
      const newMap = new Map(prevMap);
      newMap.set(filename, url);
      return newMap;
    });
  }, []);

  const getFileUrl = useCallback(
    (filename: string): string | undefined => {
      return fileUrlsMap.get(filename);
    },
    [fileUrlsMap]
  );

  const getFilesToLoad = (filePaths: string[]): string[] => {
    return filePaths.filter((i) => !fileUrlsMap.has(i));
  };

  useEffect(() => {
    return () => {
      createdUrlsRef.current.forEach((url) => {
        URL.revokeObjectURL(url);
      });
      createdUrlsRef.current.clear();
    };
  }, []);

  const contextValue = useMemo(
    () => ({
      fileUrlsMap,
      addOrUpdateFileUrl,
      getFilesToLoad,
      getFileUrl,
    }),
    [fileUrlsMap, addOrUpdateFileUrl, getFileUrl]
  );

  return <FileContext.Provider value={contextValue}>{children}</FileContext.Provider>;
};

export const useFileUrls = (requestedFilenames: string[]): Record<string, string | undefined> => {
  const { fileUrlsMap } = useFileContext();
  const previous = useRef<Record<string, string | undefined>>({});

  return useMemo(() => {
    const result: Record<string, string | undefined> = {};
    let hasChanged = false;

    for (const name of requestedFilenames) {
      const current = fileUrlsMap.get(name);
      result[name] = current;
      if (previous.current[name] !== current) hasChanged = true;
    }

    if (!hasChanged) return previous.current;

    previous.current = result;
    return result;
  }, [fileUrlsMap, requestedFilenames.join(',')]);
};

export const useFileContext = () => {
  const context = useContext(FileContext);
  if (context === undefined) {
    throw new Error('useFileContext must be used within a FileProvider');
  }
  return context;
};
