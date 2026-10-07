import { Link } from 'react-router-dom';
import { Card, EmptyState } from '../components/ui';

export default function NotFoundPage() {
  return (
    <Card>
      <EmptyState
        title="Page not found"
        message="The page you are looking for does not exist."
        action={
          <Link to="/" className="btn btn-primary">
            Back to dashboard
          </Link>
        }
      />
    </Card>
  );
}
