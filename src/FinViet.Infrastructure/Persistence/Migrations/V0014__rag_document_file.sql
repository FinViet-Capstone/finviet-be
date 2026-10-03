-- Store the original bytes of ingested knowledge-base PDFs in Postgres.
-- PdfDocumentIngestionService used to write them to wwwroot/documents on the API container's
-- local disk, which is ephemeral on Render: every redeploy/restart deleted them, so the admin
-- Knowledge Base could never preview the original file (GET /documents/{id}.pdf -> 404) even
-- though the extracted chunks in rag_chunk survived. Separate table so listing rag_document
-- never loads file bytes. Documents ingested before this migration have no row here and must be
-- re-uploaded to become previewable; their chunks keep working for RAG either way.
CREATE TABLE public.rag_document_file (
    document_id uuid NOT NULL,
    content bytea NOT NULL,
    content_type character varying(100) NOT NULL,
    size_bytes integer NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT rag_document_file_pkey PRIMARY KEY (document_id),
    CONSTRAINT rag_document_file_document_id_fkey FOREIGN KEY (document_id)
        REFERENCES public.rag_document(id) ON DELETE CASCADE,
    CONSTRAINT rag_document_file_size_bytes_check CHECK (size_bytes >= 0)
);
