#ifndef FILE_SIGNATURES_H
#define FILE_SIGNATURES_H

#include <windows.h>
#include <stdint.h>
#include <string.h>
#include <string>

/*
    ASSINATURAS DE FORMATO PARA VALIDAÇÃO DE CONTEÚDO.

    POR QUE ISTO EXISTE
    ───────────────────
    A varredura por MFT lê o NOME do arquivo. O nome mente.

    Quando um arquivo é apagado, os clusters dele são liberados e realocados
    para outro arquivo. O registro MFT continua na MFT — com nome, tamanho e
    runlist intactos — mas a runlist agora aponta para bytes que pertencem a
    outra coisa. O resultado é uma lista com nomes perfeitos e conteúdo sem
    relação nenhuma: um "contrato.docx" que abre um fragmento de vídeo.

    Isso não se resolve com filtro de nome, nem olhando se a extensão é
    plausível. Só existe uma forma de saber: ler os primeiros bytes do
    conteúdo e conferir a MAGIA do formato. Se o arquivo se diz .docx mas
    não começa com "PK\x03\x04", ele não é um .docx — é lixo, e não deve
    aparecer na lista de um usuário.

    COMO SE USA
    ───────────
    Três resultados, não dois. A distinção entre "não bateu" e "esse formato
    não tem assinatura" é essencial: .txt, .csv, .json e .log são formatos
    legítimos SEM magic number, e tratá-los como "sem assinatura" e descartá-los
    jogaria fora exatamente os arquivos que o usuário mais procura.

        Match       → a assinatura confere. Pode listar.
        Mismatch    → a assinatura NÃO confere. Descartar.
        NoSignature → formato sem magic number. Não dá para provar nada;
                      decidir por nome + tamanho (ver ValidacaoTextual).
*/

namespace Sig {

    // ---------------------------------------------------------------------
    // Tabela. A extensão é SEM PONTO e em minúsculas.
    //
    // `offset` é a posição da assinatura dentro do arquivo — não é sempre 0.
    // MP4/MOV putting "ftyp" no offset 4, e RIFF (WAV/WEBP/AVI) putting o
    // subtipo no offset 8. A entrada de RIFF é tratada pelo caso especial
    // abaixo, porque o subtipo é que identifica o formato.
    // ---------------------------------------------------------------------
    struct Entry {
        const char* ext;
        uint32_t    offset;
        const uint8_t* magic;
        uint8_t     len;
        const uint8_t* magic2;   // alternativa válida (ex.: TIFF little/big endian)
        uint8_t     len2;
    };

    #define SIG_ARR(name, ...) static const uint8_t name[] = { __VA_ARGS__ }

    // Imagens
    SIG_ARR(M_JPG,    0xFF, 0xD8, 0xFF);
    SIG_ARR(M_PNG,    0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
    SIG_ARR(M_GIF,    0x47, 0x49, 0x46, 0x38);              // "GIF8"
    SIG_ARR(M_BMP,    0x42, 0x4D);                          // "BM"
    SIG_ARR(M_ICO,    0x00, 0x00, 0x01, 0x00);
    SIG_ARR(M_PSD,    0x38, 0x42, 0x50, 0x53);              // "8BPS"
    SIG_ARR(M_TIFFL,  0x49, 0x49, 0x2A, 0x00);              // "II*\0"
    SIG_ARR(M_TIFFB,  0x4D, 0x4D, 0x00, 0x2A);              // "MM\0*"
    SIG_ARR(M_RIFF,   0x52, 0x49, 0x46, 0x46);              // "RIFF"
    SIG_ARR(M_WEBP,   0x57, 0x45, 0x42, 0x50);              // "WEBP" @ 8
    SIG_ARR(M_WAVE,   0x57, 0x41, 0x56, 0x45);              // "WAVE" @ 8
    SIG_ARR(M_AVI,    0x41, 0x56, 0x49, 0x20);              // "AVI " @ 8
    SIG_ARR(M_FTYP,   0x66, 0x74, 0x79, 0x70);              // "ftyp" @ 4
    SIG_ARR(M_MP4BR,  0x6D, 0x70, 0x34, 0x32);              // "mp42" (alguns muxers)
    SIG_ARR(M_EBML,   0x1A, 0x45, 0xDF, 0xA3);              // Matroska / WebM

    // Documentos e Office
    SIG_ARR(M_PDF,    0x25, 0x50, 0x44, 0x46);              // "%PDF"
    SIG_ARR(M_ZIP,    0x50, 0x4B, 0x03, 0x04);              // "PK\x03\x04" (docx/xlsx/zip)
    SIG_ARR(M_ZIPEMPTY, 0x50, 0x4B, 0x05, 0x06);            // zip vazio (raro, mas válido)
    SIG_ARR(M_OLE,    0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1); // doc/xls/ppt/msi
    SIG_ARR(M_RTF,    0x7B, 0x5C, 0x72, 0x74, 0x66);        // "{\rtf"
    SIG_ARR(M_SQLITE, 0x53, 0x51, 0x4C, 0x69, 0x74, 0x65, 0x20, 0x66, 0x6F, 0x72, 0x6D, 0x61, 0x74, 0x20, 0x33, 0x00);

    // Áudio
    SIG_ARR(M_ID3,    0x49, 0x44, 0x33);                    // "ID3"
    SIG_ARR(M_MP3_1,  0xFF, 0xFB);
    SIG_ARR(M_MP3_2,  0xFF, 0xF3);
    SIG_ARR(M_MP3_3,  0xFF, 0xF2);
    SIG_ARR(M_FLAC,   0x66, 0x4C, 0x61, 0x43);              // "fLaC"
    SIG_ARR(M_OGG,    0x4F, 0x67, 0x67, 0x53);              // "OggS"

    // Executáveis e binários
    SIG_ARR(M_PE,     0x4D, 0x5A);                          // "MZ"

    // Compactadores
    SIG_ARR(M_7Z,     0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C);
    SIG_ARR(M_RAR,    0x52, 0x61, 0x72, 0x21, 0x1A, 0x07);
    SIG_ARR(M_GZ,     0x1F, 0x8B);
    SIG_ARR(M_BZ2,    0x42, 0x5A, 0x68);                    // "BZh"
    SIG_ARR(M_CAB,    0x4D, 0x53, 0x43, 0x46);              // "MSCF"

    // Fontes
    SIG_ARR(M_TTF,    0x00, 0x01, 0x00, 0x00);
    SIG_ARR(M_OTTO,   0x4F, 0x54, 0x54, 0x4F);              // "OTTO"

    #undef SIG_ARR

    // ---------------------------------------------------------------------
    // A tabela. `magic2` cobre os formatos que têm duas representações
    // legítimas (TIFF) ou mais de um ponto de partida aceito.
    // ---------------------------------------------------------------------
    static const Entry TABLE[] = {
        // ---- Imagens ----
        { "jpg",  0, M_JPG,    3, nullptr, 0 },
        { "jpeg", 0, M_JPG,    3, nullptr, 0 },
        { "jpe",  0, M_JPG,    3, nullptr, 0 },
        { "png",  0, M_PNG,    8, nullptr, 0 },
        { "gif",  0, M_GIF,    4, nullptr, 0 },
        { "bmp",  0, M_BMP,    2, nullptr, 0 },
        { "ico",  0, M_ICO,    4, nullptr, 0 },
        { "psd",  0, M_PSD,    4, nullptr, 0 },
        { "tif",  0, M_TIFFL,  4, M_TIFFB, 4 },
        { "tiff", 0, M_TIFFL,  4, M_TIFFB, 4 },

        // ---- Vídeo / áudio containerizados ----
        { "mp4",  4, M_FTYP,   4, M_MP4BR, 4 },
        { "m4v",  4, M_FTYP,   4, M_MP4BR, 4 },
        { "mov",  4, M_FTYP,   4, M_MP4BR, 4 },
        { "3gp",  4, M_FTYP,   4, M_MP4BR, 4 },
        { "m4a",  4, M_FTYP,   4, M_MP4BR, 4 },
        { "mkv",  0, M_EBML,   4, nullptr, 0 },
        { "webm", 0, M_EBML,   4, nullptr, 0 },

        // ---- Documentos ----
        { "pdf",  0, M_PDF,    4, nullptr, 0 },
        { "rtf",  0, M_RTF,    5, nullptr, 0 },

        // Office novo (OOXML) e o resto da família ZIP
        { "docx", 0, M_ZIP,    4, M_ZIPEMPTY, 4 },
        { "xlsx", 0, M_ZIP,    4, M_ZIPEMPTY, 4 },
        { "pptx", 0, M_ZIP,    4, M_ZIPEMPTY, 4 },
        { "odt",  0, M_ZIP,    4, M_ZIPEMPTY, 4 },
        { "ods",  0, M_ZIP,    4, M_ZIPEMPTY, 4 },
        { "odp",  0, M_ZIP,    4, M_ZIPEMPTY, 4 },
        { "epub", 0, M_ZIP,    4, M_ZIPEMPTY, 4 },
        { "epub3",0, M_ZIP,    4, M_ZIPEMPTY, 4 },
        { "jar",  0, M_ZIP,    4, M_ZIPEMPTY, 4 },
        { "apk",  0, M_ZIP,    4, M_ZIPEMPTY, 4 },
        { "xpi",  0, M_ZIP,    4, M_ZIPEMPTY, 4 },
        { "zip",  0, M_ZIP,    4, M_ZIPEMPTY, 4 },
        { "whl",  0, M_ZIP,    4, M_ZIPEMPTY, 4 },

        // Office antigo (OLE2) e outros contêineres compound
        { "doc",  0, M_OLE,    8, nullptr, 0 },
        { "xls",  0, M_OLE,    8, nullptr, 0 },
        { "ppt",  0, M_OLE,    8, nullptr, 0 },
        { "msi",  0, M_OLE,    8, nullptr, 0 },
        { "msg",  0, M_OLE,    8, nullptr, 0 },
        { "pst",  0, M_OLE,    8, nullptr, 0 },
        { "vsd",  0, M_OLE,    8, nullptr, 0 },

        // ---- Áudio ----
        { "mp3",  0, M_ID3,    3, nullptr, 0 },
        { "flac", 0, M_FLAC,   4, nullptr, 0 },
        { "ogg",  0, M_OGG,    4, nullptr, 0 },

        // ---- Executáveis e binários ----
        { "exe",  0, M_PE,     2, nullptr, 0 },
        { "dll",  0, M_PE,     2, nullptr, 0 },
        { "sys",  0, M_PE,     2, nullptr, 0 },
        { "drv",  0, M_PE,     2, nullptr, 0 },
        { "ocx",  0, M_PE,     2, nullptr, 0 },
        { "cpl",  0, M_PE,     2, nullptr, 0 },
        { "scr",  0, M_PE,     2, nullptr, 0 },
        { "efi",  0, M_PE,     2, nullptr, 0 },

        // ---- Compactadores ----
        { "7z",   0, M_7Z,     6, nullptr, 0 },
        { "rar",  0, M_RAR,    6, nullptr, 0 },
        { "gz",   0, M_GZ,     2, nullptr, 0 },
        { "tgz",  0, M_GZ,     2, nullptr, 0 },
        { "bz2",  0, M_BZ2,    3, nullptr, 0 },
        { "cab",  0, M_CAB,    4, nullptr, 0 },

        // ---- Bases de dados ----
        { "sqlite", 0, M_SQLITE, 16, nullptr, 0 },
        { "sqlite3",0, M_SQLITE, 16, nullptr, 0 },
        { "db",     0, M_SQLITE, 16, nullptr, 0 },

        // ---- Fontes ----
        { "ttf",  0, M_TTF,    4, nullptr, 0 },
        { "otf",  0, M_OTTO,   4, nullptr, 0 },
    };

    static const size_t TABLE_COUNT = sizeof(TABLE) / sizeof(TABLE[0]);

    // ---------------------------------------------------------------------
    // Formatos SEM magic number. Não são "invalidados" — apenas não são
    // prováveis. Entram na lista com o resto, mas com recuperabilidade menor,
    // porque a validação de conteúdo não pôde rodar.
    // ---------------------------------------------------------------------
    static const char* const NO_SIGNATURE_EXTS[] = {
        // Texto e código
        "txt", "log", "md", "csv", "tsv", "json", "xml", "yaml", "yml", "ini",
        "cfg", "conf", "env", "toml", "srt", "vtt", "nfo", "diz", "asc",
        "js", "mjs", "cjs", "ts", "tsx", "jsx", "css", "scss", "less", "html",
        "htm", "vue", "svelte", "py", "pyw", "rb", "pl", "php", "java", "kt",
        "kts", "go", "rs", "cs", "c", "h", "cpp", "hpp", "cc", "lua", "r",
        "sql", "ps1", "psm1", "bat", "cmd", "sh", "bash", "reg", "inf", "url",
        // Imagens de disco e pacotes de container
        "dmg", "iso", "img", "vhd", "vhdx", "ova", "ovf",
        // Dados
        "csv2", "ndjson", "parquet", "avro", "pcap", "pcapng", "etl",
        // Fontes em formatos sem assinatura estável
        "woff", "woff2", "eot", "fon",
    };

    static const size_t NO_SIG_COUNT = sizeof(NO_SIGNATURE_EXTS) / sizeof(NO_SIGNATURE_EXTS[0]);

    inline bool EqualsNoCase(const char* a, const char* b) {
        if (!a || !b) return false;
        while (*a && *b) {
            char ca = *a, cb = *b;
            if (ca >= 'A' && ca <= 'Z') ca = (char)(ca + 32);
            if (cb >= 'A' && cb <= 'Z') cb = (char)(cb + 32);
            if (ca != cb) return false;
            a++; b++;
        }
        return *a == 0 && *b == 0;
    }

    inline bool IsNoSignatureFormat(const char* extLower) {
        for (size_t i = 0; i < NO_SIG_COUNT; ++i)
            if (EqualsNoCase(NO_SIGNATURE_EXTS[i], extLower)) return true;
        return false;
    }

    inline const Entry* Find(const char* extLower) {
        for (size_t i = 0; i < TABLE_COUNT; ++i)
            if (EqualsNoCase(TABLE[i].ext, extLower)) return &TABLE[i];
        return nullptr;
    }

    // A extensão é de um tipo que o app sabe tratar? Isso é o PORTÃO DE
    // ENTRADA da lista: extensão desconhecida nunca é listada, mesmo que o
    // conteúdo esteja intacto. A varredura por assinatura é a ferramenta
    // certa para formato exótico.
    inline bool IsKnownExtension(const char* extLower) {
        if (!extLower || !*extLower) return false;
        if (Find(extLower)) return true;
        if (IsNoSignatureFormat(extLower)) return true;

        // RIFF: o contêiner é genérico, o formato real está no offset 8. Um
        // ".wav" cujo cabeçalho é "RIFF" + "WAVE" passa; a validação fina é
        // feita em ValidateContent.
        if (EqualsNoCase(extLower, "wav") || EqualsNoCase(extLower, "webp") || EqualsNoCase(extLower, "avi"))
            return true;

        // MP3 sem tag ID3 começa por um frame sync 0xFF 0xEx, que a tabela
        // não cobre. É o único caso em que aceitamos a extensão sem
        // assinatura completa, e ainda assim a validação abaixo exige o
        // sync — ou seja, ainda é prova de conteúdo.
        if (EqualsNoCase(extLower, "mp3")) return true;

        return false;
    }

    enum class Result { Match, Mismatch, NoSignature };

    /*
        CONFERE A ASSINATURA DO CONTEÚDO CONTRA A EXTENSÃO.

        `data` são os primeiros bytes do conteúdo do arquivo. `len` é quantos
        foram lidos.
    */
    inline Result ValidateContent(const char* extLower, const uint8_t* data, size_t len) {
        if (!extLower || !data || len == 0) return Result::NoSignature;

        // ---- RIFF: o subtipo no offset 8 é o que decide ----
        if (EqualsNoCase(extLower, "wav") || EqualsNoCase(extLower, "webp") || EqualsNoCase(extLower, "avi")) {
            if (len < 12) return Result::NoSignature;   // leitura curta demais para decidir
            if (memcmp(data, M_RIFF, 4) != 0) return Result::Mismatch;
            const uint8_t* want = EqualsNoCase(extLower, "wav") ? M_WAVE
                             : EqualsNoCase(extLower, "webp") ? M_WEBP : M_AVI;
            return memcmp(data + 8, want, 4) == 0 ? Result::Match : Result::Mismatch;
        }

        // ---- MP3: aceita ID3 ou frame sync ----
        if (EqualsNoCase(extLower, "mp3")) {
            if (len >= 3 && memcmp(data, M_ID3, 3) == 0) return Result::Match;
            if (len >= 2 && data[0] == 0xFF && (data[1] & 0xE0) == 0xE0) return Result::Match;
            return Result::Mismatch;
        }

        const Entry* e = Find(extLower);
        if (!e) return Result::NoSignature;   // formato sem magic number

        if (e->offset + e->len > len) return Result::NoSignature;  // não deu para ler o suficiente
        if (memcmp(data + e->offset, e->magic, e->len) == 0) return Result::Match;
        if (e->magic2 && e->len2 && (e->offset + e->len2 <= len) &&
            memcmp(data + e->offset, e->magic2, e->len2) == 0) return Result::Match;

        return Result::Mismatch;
    }

}

#endif // FILE_SIGNATURES_H
