interface BreadcrumbProps {
  filePath: string;
}

export default function Breadcrumb({ filePath }: BreadcrumbProps) {
  const parts = filePath.split('/').filter(Boolean);

  if (parts.length === 0) {
    return null;
  }

  return (
    <div className="breadcrumb">
      {parts.map((part, index) => (
        <span key={index}>
          {index > 0 && <span className="breadcrumb-separator">›</span>}
          <span className="breadcrumb-item">{part}</span>
        </span>
      ))}
    </div>
  );
}

