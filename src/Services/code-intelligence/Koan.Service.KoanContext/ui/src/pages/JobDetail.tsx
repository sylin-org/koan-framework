import { useParams } from 'react-router-dom';

export default function JobDetail() {
  const { id } = useParams();
  return (
    <div className="min-h-screen bg-background p-8">
      <h1 className="text-3xl font-bold">Job Detail: {id}</h1>
      <p className="text-muted-foreground mt-2">Job progress page (placeholder)</p>
    </div>
  );
}
