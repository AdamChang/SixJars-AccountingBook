declare const process: { env: Record<string, string | undefined> };

process.env['TZ'] = 'Asia/Taipei';
